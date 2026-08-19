using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// Gizmos メニュー（シーンビュー右上）のコンポーネント別ギズモ ON/OFF を読む。
    ///
    /// 代替パスは SDK のギズモを止めて描き直すので、放っておくと Unity の
    /// ギズモ設定を素通りしてしまう。ユーザーが Gizmos メニューで PhysBone を
    /// 消したら、こちらも描かない（SDK のギズモと同じふるまい）。
    ///
    /// 設定は AnnotationUtility（internal）にしか無いので Reflection で読む。
    /// 読めない Unity では「全て有効」へ倒す。消せなくなるだけで、描けなくなる
    /// よりは害が少ない。
    /// </summary>
    internal static class GizmoAnnotationGate
    {
        private const double RefreshInterval = 0.2;

        private static MethodInfo _getAnnotations;
        private static FieldInfo _scriptClass;
        private static FieldInfo _gizmoEnabled;
        private static bool _resolved;
        private static bool _available;

        /// <summary>ギズモが OFF にされているクラス名（scriptClass）。</summary>
        private static readonly HashSet<string> Disabled = new HashSet<string>();

        private static double _lastRefresh = double.NegativeInfinity;

        /// <summary>この型のギズモが Gizmos メニューで有効か。</summary>
        internal static bool IsGizmoEnabled(Type componentType)
        {
            Refresh();
            return !_available || !Disabled.Contains(componentType.Name);
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            try
            {
                Type utility = typeof(Editor).Assembly.GetType("UnityEditor.AnnotationUtility");
                _getAnnotations = utility?.GetMethod("GetAnnotations",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);

                Type annotation = typeof(Editor).Assembly.GetType("UnityEditor.Annotation");
                _scriptClass = annotation?.GetField("scriptClass");
                _gizmoEnabled = annotation?.GetField("gizmoEnabled");

                _available = _getAnnotations != null && _scriptClass != null && _gizmoEnabled != null;
            }
            catch (Exception)
            {
                _available = false;
            }
        }

        private static void Refresh()
        {
            Resolve();
            if (!_available) return;

            // 毎 Repaint 読み直すには boxing が多いので、少しだけ間引く。
            // Gizmos メニューの操作はシーンビューを再描画させるため、
            // 体感ではワンテンポ遅れる程度で追従する。
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRefresh < RefreshInterval) return;
            _lastRefresh = now;

            try
            {
                Disabled.Clear();
                var annotations = (Array)_getAnnotations.Invoke(null, null);
                for (int i = 0; i < annotations.Length; i++)
                {
                    object annotation = annotations.GetValue(i);
                    if ((int)_gizmoEnabled.GetValue(annotation) != 0) continue;

                    var scriptClass = (string)_scriptClass.GetValue(annotation);
                    if (!string.IsNullOrEmpty(scriptClass)) Disabled.Add(scriptClass);
                }
            }
            catch (Exception)
            {
                // 一度でも読み損ねたら以後は「全て有効」へ倒す。
                _available = false;
                Disabled.Clear();
            }
        }
    }
}
