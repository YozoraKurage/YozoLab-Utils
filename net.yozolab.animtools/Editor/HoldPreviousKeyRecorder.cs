// この機能は Harmony（0Harmony.dll）に依存します。
// スクリプティング定義シンボル "YOZOLAB_ANIMTOOLS_HARMONY" が設定されているときだけ
// コンパイルされ、このシンボルは HarmonyDefineBootstrap が Harmony の有無を自動検出して
// 付け外しします（手動設定は不要）。Harmony が無い環境ではこのファイルは空になり、
// コンパイルは通ります。
#if YOZOLAB_ANIMTOOLS_HARMONY
using System;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEngine;

namespace YozoLab.AnimTools
{
    /// <summary>
    /// Animation ウィンドウのレコードモードでキー（ブレンドシェイプ等）を打ち込んだ際に、
    /// 打ち込む直前の状態を「打ち込んだキーの 1 フレーム前」へ自動で打ち込む機能。
    ///
    /// 目的：既存のキー（例：フレーム0）から新しいキーまで長い補間ランプが勝手に生成される
    /// のを防ぎ、「直前まで元の値を保持 → そのフレームで変化」という意図どおりのカーブにする。
    ///
    /// 実装：Unity 内部の <c>UnityEditorInternal.AnimationRecording.AddKey</c> を Harmony で
    /// パッチし、キー追加の直前に編集前カーブの (currentFrame-1) の値を退避、直後にその値を
    /// (currentFrame-1) へ打ち込む。1 本目のキー（既存カーブなし）はランプが出ないので何もしない。
    ///
    /// メニュー <c>YozoLab/Animation/前フレーム自動キー (レコード時)</c> でいつでも切り替え可能。
    /// 内部 API 依存のため、失敗しても既存機能を壊さないよう全て握りつぶす。
    /// </summary>
    [InitializeOnLoad]
    public static class HoldPreviousKeyRecorder
    {
        private const string MenuPath = "YozoLab/Animation/前フレーム自動キー (レコード時)";
        private const string Pref = "YozoLab.AnimTools.HoldPrevKey.Enabled";
        private const string HarmonyId = "net.yozolab.animtools.holdprevkey";

        /// <summary>機能の有効/無効。パッチは常駐し、OFF のときは即 no-op。</summary>
        public static bool Enabled { get; private set; }

        private static bool patched;

        // AnimationWindowState のメンバ読み取り用キャッシュ
        private static PropertyInfo clipProp;
        private static PropertyInfo timeProp;

        static HoldPreviousKeyRecorder()
        {
            Enabled = EditorPrefs.GetBool(Pref, false);
            EditorApplication.delayCall += () => Menu.SetChecked(MenuPath, Enabled);
            TryApplyPatch();
        }

        // ---------------------------------------------------------------
        // メニュー（トグル）
        // ---------------------------------------------------------------

        [MenuItem(MenuPath, false, 100)]
        private static void Toggle() => SetEnabled(!Enabled);

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        /// <summary>メニュー・Animation ウィンドウのボタン双方から使う共通の切り替え処理。</summary>
        private static void SetEnabled(bool value)
        {
            Enabled = value;
            EditorPrefs.SetBool(Pref, Enabled);
            Menu.SetChecked(MenuPath, Enabled);
            if (Enabled && !patched)
            {
                // 何らかの理由で未パッチなら再試行
                TryApplyPatch();
                if (!patched)
                    Debug.LogWarning("[HoldPrevKey] レコード処理へのパッチに失敗しているため機能しません。");
            }
        }

        // ---------------------------------------------------------------
        // Harmony パッチ適用
        // ---------------------------------------------------------------

        private static void TryApplyPatch()
        {
            if (patched) return;
            try
            {
                Type recType = typeof(Editor).Assembly.GetType("UnityEditorInternal.AnimationRecording");
                MethodInfo addKey = FindAddKey(recType);
                if (addKey == null)
                {
                    Debug.LogWarning("[HoldPrevKey] AnimationRecording.AddKey が見つかりませんでした（Unity のバージョン差異）。機能を無効化します。");
                    return;
                }

                var harmony = new Harmony(HarmonyId);
                BindingFlags sf = BindingFlags.Static | BindingFlags.NonPublic;
                harmony.Patch(
                    addKey,
                    prefix: new HarmonyMethod(typeof(HoldPreviousKeyRecorder).GetMethod(nameof(AddKeyPrefix), sf)),
                    postfix: new HarmonyMethod(typeof(HoldPreviousKeyRecorder).GetMethod(nameof(AddKeyPostfix), sf)));
                patched = true;

                // Animation ウィンドウのツールバーに ON/OFF ボタンを描くための追加パッチ（任意・失敗容認）
                TryPatchWindowButton(harmony, sf);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HoldPrevKey] パッチ適用に失敗しました（機能は無効のまま）: {e.Message}");
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
                    postfix: new HarmonyMethod(typeof(HoldPreviousKeyRecorder).GetMethod(nameof(AnimationWindowOnGUIPostfix), sf)));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HoldPrevKey] Animation ウィンドウへのボタン追加に失敗しました（メニューは使用可）: {e.Message}");
            }
        }

        /// <summary>Animation ウィンドウのツールバー右側に ON/OFF トグルボタンを描く。</summary>
        private static void AnimationWindowOnGUIPostfix(EditorWindow __instance)
        {
            try
            {
                const float w = 118f, h = 18f;
                var rect = new Rect(__instance.position.width - w - 4f, 1f, w, h);
                var label = new GUIContent(
                    Enabled ? "前F自動キー: ON" : "前F自動キー: OFF",
                    "レコード時、打ったキーの1フレーム前に直前の値を自動で打ち込みます。");
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

        private static MethodInfo FindAddKey(Type recType)
        {
            if (recType == null) return null;
            MethodInfo best = null;
            foreach (MethodInfo m in recType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (m.Name != "AddKey") continue;
                // EditorCurveBinding を引数に取るものを優先（記録キー追加の本体）
                foreach (ParameterInfo p in m.GetParameters())
                {
                    if (p.ParameterType == typeof(EditorCurveBinding)) return m;
                }
                best ??= m;
            }
            return best;
        }

        // ---------------------------------------------------------------
        // パッチ本体
        // ---------------------------------------------------------------

        /// <summary>
        /// キー追加の直前。編集前カーブから (currentFrame-1) の値を退避する。
        /// 引数 state は AnimationWindowState（内部型のため object で受けてリフレクションで読む）。
        /// state / binding は AddKey の実引数名に一致させて Harmony にインジェクトさせている。
        /// </summary>
        private static void AddKeyPrefix(object state, EditorCurveBinding binding, out object __state)
        {
            __state = null;
            if (!Enabled) return;
            try
            {
                if (binding.isPPtrCurve) return; // オブジェクト参照カーブは対象外
                if (state == null) return;

                AnimationClip clip = GetClip(state);
                if (clip == null) return;

                float frameRate = clip.frameRate > 0f ? clip.frameRate : 60f;
                float currentTime = GetCurrentTime(state);
                int frameN = Mathf.RoundToInt(currentTime * frameRate);
                if (frameN < 1) return; // 先頭フレームには前フレームが無い

                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                // 既存カーブが無い＝1本目のキー。単一キーはランプにならないので何もしない。
                if (curve == null || curve.length == 0) return;

                float timePrev = (frameN - 1) / frameRate;
                if (HasKeyAt(curve, timePrev)) return; // 既に前フレームにキーがあるなら不要

                float prevValue = curve.Evaluate(timePrev);
                __state = new HoldInfo { clip = clip, binding = binding, time = timePrev, value = prevValue };
            }
            catch
            {
                __state = null;
            }
        }

        /// <summary>キー追加の直後。退避した値を (currentFrame-1) に打ち込む。</summary>
        private static void AddKeyPostfix(object __state)
        {
            if (!(__state is HoldInfo h)) return;
            try
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(h.clip, h.binding);
                if (curve == null) return;
                if (HasKeyAt(curve, h.time)) return; // 二重挿入防止

                curve.AddKey(new Keyframe(h.time, h.value));
                AnimationUtility.SetEditorCurve(h.clip, h.binding, curve);
            }
            catch
            {
                // 記録処理中の書き込み失敗は無視（機能を優先して既存動作を壊さない）
            }
        }

        private struct HoldInfo
        {
            public AnimationClip clip;
            public EditorCurveBinding binding;
            public float time;
            public float value;
        }

        // ---------------------------------------------------------------
        // AnimationWindowState 読み取り（リフレクション）
        // ---------------------------------------------------------------

        private static AnimationClip GetClip(object state)
        {
            if (clipProp == null || clipProp.DeclaringType?.IsInstanceOfType(state) == false)
                clipProp = state.GetType().GetProperty("activeAnimationClip",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return clipProp?.GetValue(state) as AnimationClip;
        }

        private static float GetCurrentTime(object state)
        {
            if (timeProp == null || timeProp.DeclaringType?.IsInstanceOfType(state) == false)
                timeProp = state.GetType().GetProperty("currentTime",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object v = timeProp?.GetValue(state);
            return v is float f ? f : (v is double d ? (float)d : 0f);
        }

        private static bool HasKeyAt(AnimationCurve curve, float time)
        {
            Keyframe[] keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
                if (Mathf.Abs(keys[i].time - time) < 1e-5f) return true;
            return false;
        }
    }
}
#endif // YOZOLAB_ANIMTOOLS_HARMONY
