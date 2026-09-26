using System;
using UnityEngine;
using YozoLab.SPS.Exceptions;

namespace YozoLab.SPS.Utils {
    internal class VRCFuryBuildContext : IDisposable {
        private readonly IDisposable assetEditing;
        private bool disposed;

        public VRCFuryBuildContext() {
            // VRCFury はここで Unity 本体に Harmony のパッチを当て、マテリアルの描画処理やアセットの
            // インポート後処理を止めていた（速度のため）。エディタ全体に作用するので持ち込まない。
            assetEditing = VRCFuryAssetDatabase.WithAssetEditing();

            // If we don't do this, a unity issue in RepaintImmediately can randomly throw a segfault
            RenderTexture.active = null;
            Camera.SetupCurrent(null);
        }

        public static bool Run(Action action) {
            return VRCFExceptionUtils.ErrorDialogBoundary(() => {
                using (new VRCFuryBuildContext()) {
                    action();
                }
            });
        }

        public void Dispose() {
            if (disposed) return;
            disposed = true;
            assetEditing.Dispose();
        }
    }
}
