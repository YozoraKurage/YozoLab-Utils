// この機能は Harmony（0Harmony.dll）に依存します。
// Harmony を同梱する VRCSDK（com.vrchat.base）がある環境でだけコンパイルされ、
// 判定は asmdef の versionDefines に任せています（手動設定は不要）。
// シンボル "YOZOLAB_ANIMTOOLS_HARMONY" はこのアセンブリのコンパイル時のみ有効で、
// プロジェクトの Scripting Define Symbols には何も書き込みません。
// Harmony が無い環境ではこのファイルは空になり、コンパイルは通ります。
#if YOZOLAB_ANIMTOOLS_HARMONY
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEngine;

namespace YozoLab.AnimTools
{
    /// <summary>
    /// Animation ウィンドウの右上ツールバーに、今 編集中の <see cref="AnimationClip"/> を
    /// Project ウィンドウで ping（点滅ハイライト）しつつ選択するボタンを追加する機能。
    ///
    /// 目的：現在どの Clip を編集しているのかを、ワンクリックで Project 上に辿れるようにする。
    ///
    /// 実装：Unity 内部の <c>UnityEditor.AnimationWindow.OnGUI</c> を Harmony で Postfix し、
    /// 既存「前フレーム自動キー」トグルの左隣にアイコンボタンをオーバーレイ描画する。
    /// 編集中 Clip は <c>AnimationWindow.m_AnimEditor.state.activeAnimationClip</c> を
    /// リフレクションで辿って取得する。内部 API 依存のため、失敗しても既存機能や
    /// Animation ウィンドウを壊さないよう全て握りつぶす。
    /// </summary>
    [InitializeOnLoad]
    internal static class AnimationWindowClipPing
    {
        private const string HarmonyId = "net.yozolab.animtools.clipping";

        // ping ボタン自身のレイアウト。x はトグルの左隣に置く（トグル位置は
        // HoldPreviousKeyRecorder の共通定数を基準にする）。
        private const float ButtonWidth = 24f;
        private const float Gap = 2f;

        // 内部 API 読み取り用リフレクションキャッシュ
        private static FieldInfo animEditorField;   // AnimationWindow.m_AnimEditor
        private static PropertyInfo stateProp;      // AnimEditor.state
        private static PropertyInfo clipProp;       // AnimationWindowState.activeAnimationClip

        static AnimationWindowClipPing()
        {
            TryApplyPatch();
        }

        /// <summary>AnimationWindow.OnGUI を Postfix して、ping ボタンをオーバーレイ描画する。</summary>
        private static void TryApplyPatch()
        {
            try
            {
                System.Type winType = typeof(Editor).Assembly.GetType("UnityEditor.AnimationWindow");
                MethodInfo onGui = winType?.GetMethod("OnGUI",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null, System.Type.EmptyTypes, null);
                if (onGui == null) return; // 内部 API が見つからない場合は静かに諦める

                var harmony = new Harmony(HarmonyId);
                harmony.Patch(
                    onGui,
                    postfix: new HarmonyMethod(typeof(AnimationWindowClipPing).GetMethod(
                        nameof(OnGUIPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ClipPing] Animation ウィンドウへのボタン追加に失敗しました: {e.Message}");
            }
        }

        /// <summary>Animation ウィンドウ右上、既存トグルの左隣に Clip ping ボタンを描く。</summary>
        private static void OnGUIPostfix(EditorWindow __instance)
        {
            try
            {
                float toggleLeft = __instance.position.width
                    - HoldPreviousKeyRecorder.ToolbarRightMargin
                    - HoldPreviousKeyRecorder.ToolbarButtonWidth;
                var rect = new Rect(
                    toggleLeft - Gap - ButtonWidth,
                    HoldPreviousKeyRecorder.ToolbarTop, ButtonWidth, HoldPreviousKeyRecorder.ToolbarHeight);

                AnimationClip clip = GetActiveClip(__instance);
                // ping しても意味があるのは Project 上の永続アセットのみ。
                bool canPing = clip != null && EditorUtility.IsPersistent(clip);

                var content = new GUIContent(
                    GetClipIcon(clip),
                    "編集中のClipをProjectで表示・選択 (ping)\n" + (clip != null ? clip.name : "(なし)"));

                using (new EditorGUI.DisabledScope(!canPing))
                {
                    if (GUI.Button(rect, content, EditorStyles.toolbarButton) && canPing)
                    {
                        Selection.activeObject = clip;
                        EditorGUIUtility.PingObject(clip);
                        __instance.Repaint();
                    }
                }
            }
            catch
            {
                // 描画中の例外でウィンドウを壊さないよう握りつぶす。
            }
        }

        /// <summary>Clip のミニアイコン（無ければ汎用 AnimationClip アイコン）を返す。</summary>
        private static Texture GetClipIcon(AnimationClip clip)
        {
            try
            {
                Texture icon = clip != null
                    ? EditorGUIUtility.ObjectContent(clip, typeof(AnimationClip)).image
                    : null;
                if (icon == null) icon = EditorGUIUtility.IconContent("AnimationClip Icon").image;
                return icon;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// AnimationWindow から編集中 Clip を取得する。
        /// AnimationWindow.m_AnimEditor → AnimEditor.state → AnimationWindowState.activeAnimationClip
        /// の順にリフレクションで辿る（Unity 2022.3 内部 API）。失敗時は null。
        /// </summary>
        private static AnimationClip GetActiveClip(EditorWindow window)
        {
            try
            {
                if (window == null) return null;

                if (animEditorField == null || animEditorField.DeclaringType?.IsInstanceOfType(window) == false)
                    animEditorField = window.GetType().GetField("m_AnimEditor",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                object animEditor = animEditorField?.GetValue(window);
                if (animEditor == null) return null;

                if (stateProp == null || stateProp.DeclaringType?.IsInstanceOfType(animEditor) == false)
                    stateProp = animEditor.GetType().GetProperty("state",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object state = stateProp?.GetValue(animEditor);
                if (state == null) return null;

                if (clipProp == null || clipProp.DeclaringType?.IsInstanceOfType(state) == false)
                    clipProp = state.GetType().GetProperty("activeAnimationClip",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return clipProp?.GetValue(state) as AnimationClip;
            }
            catch
            {
                return null;
            }
        }
    }
}
#endif // YOZOLAB_ANIMTOOLS_HARMONY
