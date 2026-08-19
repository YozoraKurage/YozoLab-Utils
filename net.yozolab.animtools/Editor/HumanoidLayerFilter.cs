// この機能は Harmony（0Harmony.dll）に依存します。
// Harmony を同梱する VRCSDK（com.vrchat.base）がある環境でだけコンパイルされ、
// 判定は asmdef の versionDefines に任せています（手動設定は不要）。
// Harmony が無い環境ではこのファイルは空になり、コンパイルは通ります。
#if YOZOLAB_ANIMTOOLS_HARMONY
using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEditor;
using UnityEngine;

namespace YozoLab.AnimTools
{
    /// <summary>
    /// Animation ウィンドウ上で、Humanoid 由来のキー（Animator 型バインディング＝マッスルと
    /// ルートモーション）を「別レイヤーへ退避した」ように見せる機能。
    ///
    /// 目的：表情（ブレンドシェイプ）を打ち込む際、同じ Clip に同居している体の動きを
    /// 再生はさせたまま、編集面からだけ追い出す。体のキーは編集対象ではないのに行を
    /// 埋め尽くし、表情のカーブが読めなくなるため。
    ///
    /// 実装は二本立て：
    ///   (1) 表示フィルタ … <c>AnimationWindowState.allCurves</c> の結果から Animator 型の
    ///       カーブを取り除く。Animation ウィンドウの階層・ドープシート・カーブビュー・
    ///       サマリ行はすべてこの 1 本のプロパティから組み立てられているので、ここを絞れば
    ///       下流の表示が構造的に一致する（描画後に潰す方式だと選択やキー範囲が
    ///       元の行数のまま残り、内部状態と見た目が食い違う）。
    ///   (2) 録画ロック … レコード中にシーンで体を動かしても Animator 型バインディングへ
    ///       キーが打たれないようにする。<c>AnimationRecording.AddKey</c> に加えて
    ///       <c>ProcessRootMotionModification</c> も塞ぐ（後者は AddKey を経由せず
    ///       自前で RootT/RootQ を書き込むため、AddKey だけでは穴が空く）。
    ///
    /// プレビュー再生は <c>AnimationWindowControl.ResampleAnimation</c> が
    /// <c>AnimationMode.SampleAnimationClip</c> で Clip 本体を丸ごとサンプルしているため、
    /// allCurves から外したキーも従来どおり再生される。これが本機能の前提。
    ///
    /// メニュー <c>YozoLab/Animation/Humanoid キーを隠す (擬似レイヤー)</c> と Animation
    /// ウィンドウ上のトグルで、いつでもワンクリックで往復できる。「Humanoid のキーを
    /// 編集したくなることは無い」という前提が外れたときの逃げ道なので、この即時性は
    /// モーダルな設定項目へ後退させないこと。
    ///
    /// 内部 API 依存のため、失敗しても Animation ウィンドウを壊さないよう全て握りつぶす。
    /// </summary>
    [InitializeOnLoad]
    public static class HumanoidLayerFilter
    {
        private const string MenuPath = "YozoLab/Animation/Humanoid キーを隠す (擬似レイヤー)";
        private const string Pref = "YozoLab.AnimTools.HumanoidLayer.Enabled";
        private const string HarmonyId = "net.yozolab.animtools.humanoidlayer";

        // ツールバー上のトグル。Clip ping ボタンのさらに左隣に並べる。
        internal const float ToolbarButtonWidth = 116f;
        internal const float Gap = 2f;

        /// <summary>機能の有効/無効。パッチは常駐し、OFF のときは即 no-op。</summary>
        public static bool Enabled { get; private set; }

        private static bool patched;

        // 内部 API 読み取り用リフレクションキャッシュ
        private static Type stateType;              // UnityEditorInternal.AnimationWindowState
        private static MethodInfo allCurvesGetter;  // AnimationWindowState.get_allCurves
        private static FieldInfo allCurvesCache;    // AnimationWindowState.m_AllCurvesCache
        private static PropertyInfo curveBindingProp; // AnimationWindowCurve.binding

        // 絞り込み済みリストの記憶。allCurves は 1 フレームに何度も、しかもループの中から
        // 読まれるので、同じインスタンスが返ってきている間は走査ごと省く。
        private static readonly ConditionalWeakTable<object, object> FilteredByState =
            new ConditionalWeakTable<object, object>();

        // allCurves の中で元の getter を呼び直すための再入ガード。
        private static bool reentrant;

        static HumanoidLayerFilter()
        {
            Enabled = EditorPrefs.GetBool(Pref, false);
            EditorApplication.delayCall += () => Menu.SetChecked(MenuPath, Enabled);
            TryApplyPatch();
        }

        // ---------------------------------------------------------------
        // 他機能との取り決め
        // ---------------------------------------------------------------

        /// <summary>
        /// このバインディングが今ロックされている（＝隠されていて書き込んではならない）か。
        /// 録画まわりに手を入れる他機能は、キーを書く前に必ずこれを尋ねること。
        /// </summary>
        public static bool IsLocked(EditorCurveBinding binding) => Enabled && IsHumanoid(binding);

        /// <summary>Humanoid 由来か。Clip 内の Animator 型バインディングはマッスルとルートモーションのみ。</summary>
        private static bool IsHumanoid(EditorCurveBinding binding) => binding.type == typeof(Animator);

        // ---------------------------------------------------------------
        // メニュー（トグル）
        // ---------------------------------------------------------------

        [MenuItem(MenuPath, false, 101)]
        private static void Toggle() => SetEnabled(!Enabled);

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        /// <summary>メニュー・Animation ウィンドウのトグル双方から使う共通の切り替え処理。</summary>
        private static void SetEnabled(bool value)
        {
            Enabled = value;
            EditorPrefs.SetBool(Pref, Enabled);
            Menu.SetChecked(MenuPath, Enabled);

            if (Enabled && !patched)
            {
                TryApplyPatch();
                if (!patched)
                    Debug.LogWarning("[HumanoidLayer] Animation ウィンドウへのパッチに失敗しているため機能しません。");
            }

            AnimationWindowRefresher.RefreshAll();
        }

        // ---------------------------------------------------------------
        // Harmony パッチ適用
        // ---------------------------------------------------------------

        private static void TryApplyPatch()
        {
            if (patched) return;
            try
            {
                Assembly editorAsm = typeof(Editor).Assembly;

                stateType = editorAsm.GetType("UnityEditorInternal.AnimationWindowState");
                allCurvesGetter = stateType?.GetProperty("allCurves",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(true);
                allCurvesCache = stateType?.GetField("m_AllCurvesCache",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (allCurvesGetter == null || allCurvesCache == null)
                {
                    Debug.LogWarning("[HumanoidLayer] AnimationWindowState.allCurves が見つかりませんでした（Unity のバージョン差異）。機能を無効化します。");
                    return;
                }


                var harmony = new Harmony(HarmonyId);
                BindingFlags sf = BindingFlags.Static | BindingFlags.NonPublic;

                harmony.Patch(
                    allCurvesGetter,
                    prefix: new HarmonyMethod(typeof(HumanoidLayerFilter).GetMethod(nameof(AllCurvesPrefix), sf)));
                patched = true;

                TryPatchRecordingGuard(harmony, editorAsm, sf);
                TryPatchWindowButton(harmony, sf);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HumanoidLayer] パッチ適用に失敗しました（機能は無効のまま）: {e.Message}");
            }
        }

        /// <summary>
        /// 録画ロック。Animator 型バインディングへの書き込み経路を塞ぐ。
        /// ルートモーションは AddKey を通らず自前で書き込むため、別途 Prefix が要る。
        /// </summary>
        private static void TryPatchRecordingGuard(Harmony harmony, Assembly editorAsm, BindingFlags sf)
        {
            try
            {
                Type recType = editorAsm.GetType("UnityEditorInternal.AnimationRecording");
                if (recType == null) return;

                BindingFlags mf = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
                var bindingGuard = new HarmonyMethod(
                    typeof(HumanoidLayerFilter).GetMethod(nameof(BindingWriteGuard), sf));

                foreach (MethodInfo m in recType.GetMethods(mf))
                {
                    if (m.Name != "AddKey" && m.Name != "AddRotationKey") continue;
                    // binding 引数を持つものだけが対象（引数名で Harmony にインジェクトさせる）
                    if (Array.Find(m.GetParameters(), p => p.ParameterType == typeof(EditorCurveBinding)) == null) continue;
                    harmony.Patch(m, prefix: bindingGuard);
                }

                MethodInfo rootMotion = recType.GetMethod("ProcessRootMotionModification", mf);
                if (rootMotion != null)
                {
                    harmony.Patch(
                        rootMotion,
                        prefix: new HarmonyMethod(typeof(HumanoidLayerFilter).GetMethod(nameof(RootMotionWriteGuard), sf)));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HumanoidLayer] 録画ロックの設置に失敗しました（表示フィルタのみ有効）: {e.Message}");
            }
        }

        /// <summary>AnimationWindow.OnGUI を Postfix して、トグルボタンをオーバーレイ描画する。</summary>
        private static void TryPatchWindowButton(Harmony harmony, BindingFlags sf)
        {
            try
            {
                Type winType = typeof(Editor).Assembly.GetType("UnityEditor.AnimationWindow");
                MethodInfo onGui = winType?.GetMethod("OnGUI",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null, Type.EmptyTypes, null);
                if (onGui == null) return; // ボタンは無くてもメニューで切り替え可能なので致命的ではない

                harmony.Patch(
                    onGui,
                    postfix: new HarmonyMethod(typeof(HumanoidLayerFilter).GetMethod(nameof(AnimationWindowOnGUIPostfix), sf)));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HumanoidLayer] Animation ウィンドウへのボタン追加に失敗しました（メニューは使用可）: {e.Message}");
            }
        }

        // ---------------------------------------------------------------
        // 表示フィルタ本体
        // ---------------------------------------------------------------

        /// <summary>
        /// allCurves の getter の前で、内部キャッシュ <c>m_AllCurvesCache</c> を絞り込み済みの
        /// リストへ差し替える。差し替えた後は元の getter が「キャッシュあり」としてそれを
        /// そのまま返すため、戻り値には一切触らずに済む。
        ///
        /// キャッシュが空のときは元の getter を一度呼んで組み立てさせる（再入ガードで
        /// このプレフィックス自身は素通りする）。内部の組み立てロジックを写し取らないのは、
        /// 読み取り専用 Clip の扱いなど Unity 側の条件を二重に持ちたくないため。
        /// </summary>
        private static void AllCurvesPrefix(object __instance)
        {
            if (!Enabled || reentrant || __instance == null) return;

            reentrant = true;
            try
            {
                object cache = allCurvesCache.GetValue(__instance);
                if (cache == null)
                {
                    allCurvesGetter.Invoke(__instance, null); // 元の実装に組み立てさせる
                    cache = allCurvesCache.GetValue(__instance);
                }

                if (!(cache is IList source)) return;

                // 既に自分が差し替えたインスタンスなら走査ごと省く。
                if (FilteredByState.TryGetValue(__instance, out object known) && ReferenceEquals(known, cache)) return;

                if (!ContainsHumanoid(source)) return;

                IList filtered = BuildFiltered(source);
                if (filtered == null) return;

                allCurvesCache.SetValue(__instance, filtered);
                FilteredByState.Remove(__instance);
                FilteredByState.Add(__instance, filtered);
            }
            catch
            {
                // 失敗しても素の（絞り込まれていない）表示に戻るだけ。
            }
            finally
            {
                reentrant = false;
            }
        }

        private static bool ContainsHumanoid(IList curves)
        {
            for (int i = 0; i < curves.Count; i++)
            {
                if (TryGetBinding(curves[i], out EditorCurveBinding binding) && IsHumanoid(binding))
                    return true;
            }
            return false;
        }

        /// <summary>元と同じ具象型の空リストを作り、Humanoid 以外を詰め直す。</summary>
        private static IList BuildFiltered(IList source)
        {
            var filtered = Activator.CreateInstance(source.GetType()) as IList;
            if (filtered == null) return null;

            for (int i = 0; i < source.Count; i++)
            {
                object curve = source[i];
                if (TryGetBinding(curve, out EditorCurveBinding binding) && IsHumanoid(binding)) continue;
                filtered.Add(curve);
            }
            return filtered;
        }

        /// <summary>AnimationWindowCurve.binding をリフレクションで読む（内部型のため object 経由）。</summary>
        private static bool TryGetBinding(object curve, out EditorCurveBinding binding)
        {
            binding = default;
            if (curve == null) return false;
            try
            {
                if (curveBindingProp == null || curveBindingProp.DeclaringType?.IsInstanceOfType(curve) == false)
                    curveBindingProp = curve.GetType().GetProperty("binding",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                object value = curveBindingProp?.GetValue(curve);
                if (!(value is EditorCurveBinding b)) return false;
                binding = b;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ---------------------------------------------------------------
        // 録画ロック本体
        // ---------------------------------------------------------------

        /// <summary>Animator 型バインディングへのキー追加を握り潰す。false = 元の処理を実行しない。</summary>
        private static bool BindingWriteGuard(EditorCurveBinding binding) => !IsLocked(binding);

        /// <summary>
        /// ルートモーション（RootT/RootQ 等）の書き込みを握り潰す。この経路は
        /// binding を自前で組み立てるため引数から受け取れず、常に Animator 型になる。
        /// </summary>
        private static bool RootMotionWriteGuard() => !Enabled;

        // ---------------------------------------------------------------
        // ツールバーのトグル
        // ---------------------------------------------------------------

        /// <summary>Animation ウィンドウのツールバー右側、Clip ping ボタンの左隣にトグルを描く。</summary>
        private static void AnimationWindowOnGUIPostfix(EditorWindow __instance)
        {
            try
            {
                float pingLeft = __instance.position.width
                    - HoldPreviousKeyRecorder.ToolbarRightMargin
                    - HoldPreviousKeyRecorder.ToolbarButtonWidth
                    - AnimationWindowClipPing.Gap
                    - AnimationWindowClipPing.ButtonWidth;

                var rect = new Rect(
                    pingLeft - Gap - ToolbarButtonWidth,
                    HoldPreviousKeyRecorder.ToolbarTop, ToolbarButtonWidth, HoldPreviousKeyRecorder.ToolbarHeight);

                var label = new GUIContent(
                    Enabled ? "Humanoid: 非表示" : "Humanoid: 表示",
                    "Humanoid（マッスル・ルートモーション）のキーを Animation ウィンドウから隠し、\n"
                    + "レコード中の書き込みも禁止します。再生には従来どおり反映されます。");

                bool now = GUI.Toggle(rect, Enabled, label, EditorStyles.toolbarButton);
                if (now != Enabled)
                {
                    SetEnabled(now);
                    __instance.Repaint();
                }
            }
            catch
            {
                // 描画中の例外でウィンドウを壊さないよう握りつぶす。
            }
        }
    }
}
#endif // YOZOLAB_ANIMTOOLS_HARMONY
