using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace YozoLab.AnimTools
{
    /// <summary>
    /// 開いている Animation ウィンドウに、表示の作り直しを要求する共通処理。
    ///
    /// 単なる Repaint では足りない場面がある。行の一覧と各行の表示名は
    /// <c>AnimationWindowHierarchyDataSource.FetchData</c> の時点で組み立てられて
    /// ノードに焼き付くため、表示のしかたを変える設定を切り替えたときは、
    /// 階層そのものを組み直させる必要がある。<c>AnimationWindowState.refresh</c> に
    /// <c>RefreshType.Everything</c> を立てるとそれが起きる。
    ///
    /// 内部 API 依存のため、失敗しても黙って諦める（次の再描画で追い付く）。
    /// </summary>
    internal static class AnimationWindowRefresher
    {
        private static PropertyInfo refreshProp;    // AnimationWindowState.refresh
        private static object refreshEverything;    // AnimationWindowState.RefreshType.Everything
        private static FieldInfo animEditorField;   // AnimationWindow.m_AnimEditor
        private static PropertyInfo stateProp;      // AnimEditor.state
        private static bool resolved;

        /// <summary>開いている全ての Animation ウィンドウを作り直させる。</summary>
        public static void RefreshAll()
        {
            try
            {
                Type winType = typeof(Editor).Assembly.GetType("UnityEditor.AnimationWindow");
                if (winType == null) return;

                Resolve();

                foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(winType))
                {
                    var window = obj as EditorWindow;
                    if (window == null) continue;

                    object state = GetState(window);
                    if (state != null && refreshProp != null && refreshEverything != null)
                        refreshProp.SetValue(state, refreshEverything);

                    window.Repaint();
                }
            }
            catch
            {
                // 表示の更新に失敗しても、設定そのものは切り替わっている。
            }
        }

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;

            try
            {
                Type stateType = typeof(Editor).Assembly.GetType("UnityEditorInternal.AnimationWindowState");
                if (stateType == null) return;

                refreshProp = stateType.GetProperty("refresh",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                Type refreshType = stateType.GetNestedType("RefreshType", BindingFlags.Public | BindingFlags.NonPublic);
                if (refreshType != null)
                    refreshEverything = Enum.Parse(refreshType, "Everything");
            }
            catch
            {
                refreshProp = null;
                refreshEverything = null;
            }
        }

        /// <summary>AnimationWindow.m_AnimEditor → AnimEditor.state と辿る。失敗時は null。</summary>
        private static object GetState(EditorWindow window)
        {
            try
            {
                if (animEditorField == null || animEditorField.DeclaringType?.IsInstanceOfType(window) == false)
                    animEditorField = window.GetType().GetField("m_AnimEditor",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                object animEditor = animEditorField?.GetValue(window);
                if (animEditor == null) return null;

                if (stateProp == null || stateProp.DeclaringType?.IsInstanceOfType(animEditor) == false)
                    stateProp = animEditor.GetType().GetProperty("state",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return stateProp?.GetValue(animEditor);
            }
            catch
            {
                return null;
            }
        }
    }
}
