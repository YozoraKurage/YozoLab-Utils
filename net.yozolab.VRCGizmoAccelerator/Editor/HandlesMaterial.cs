using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// Handles が線を描くときに使うマテリアルを適用する。
    ///
    /// UnityEditor.HandleUtility.ApplyWireMaterial(CompareFunction) は internal なので
    /// Reflection で呼ぶ。SDK 側も同じことをしているが、あちらは線 1 本ごとに
    /// MethodInfo.Invoke している（引数配列と boxing のアロケーション込み）。
    /// こちらは 1 パスに 1 回しか呼ばないうえ、デリゲートに束ねて Invoke のコストも消す。
    /// </summary>
    internal static class HandlesMaterial
    {
        private delegate void ApplyWireMaterialDelegate(CompareFunction zTest);

        private static ApplyWireMaterialDelegate _apply;
        private static bool _resolved;

        internal static bool Available
        {
            get { Resolve(); return _apply != null; }
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            var method = typeof(HandleUtility).GetMethod(
                "ApplyWireMaterial",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(CompareFunction) },
                null);

            if (method == null) return;

            try
            {
                _apply = (ApplyWireMaterialDelegate)Delegate.CreateDelegate(
                    typeof(ApplyWireMaterialDelegate), method);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VRC Gizmo Accelerator] HandleUtility.ApplyWireMaterial を束ねられなかった: {e.Message}");
                _apply = null;
            }
        }

        internal static void Apply()
        {
            Resolve();
            _apply?.Invoke(Handles.zTest);
        }
    }
}
