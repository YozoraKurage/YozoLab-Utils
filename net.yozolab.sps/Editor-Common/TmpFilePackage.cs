using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.PackageManager;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Utils;

namespace YozoLab.SPS {
    /**
     * SPS がビルド中にディスクへ書き出す必要のあるもの（加工したシェーダー）の置き場所。
     *
     * VRCFury は Packages/ の下に一時パッケージを作っていたが、パッケージの解決は非同期なので
     * エディタ起動時に先回りして作っておく必要があった（SPS を使わないプロジェクトにも作られる）。
     * ここでは Assets/ の下に、必要になったときだけ作る。フォルダの作成はその場で終わる。
     * 中身はビルドのたびに作り直せるので、消してもかまわない。
     */
    internal static class TmpFilePackage {
        private const string TmpDirPath = "Assets/YozoLab SPS Generated";

        public static void Cleanup(ISet<string> usedFolders = null) {
            var tmpDir = GetPathNullable();
            if (tmpDir == null) return;
            var buildsDir = tmpDir + "/Builds";
            if (!AssetDatabase.IsValidFolder(buildsDir)) return;

            VRCFuryAssetDatabase.WithAssetEditing(() => {
                VRCFuryAssetDatabase.DeleteFiltered(buildsDir, path => {
                    if (usedFolders != null && usedFolders.Any(used => path.StartsWith($"{used}/") || path == used || used.StartsWith($"{path}/"))) return false;
                    return true;
                });
            });
            VRCFuryAssetDatabase.WithoutAssetEditing(() => {});
        }

        [CanBeNull]
        public static string GetPathNullable() {
            if (!AssetDatabase.IsValidFolder(TmpDirPath)) return null;
            return TmpDirPath;
        }

        public static string GetPath() {
            if (!AssetDatabase.IsValidFolder(TmpDirPath)) {
                AssetDatabase.CreateFolder("Assets", "YozoLab SPS Generated");
            }
            return TmpDirPath;
        }
    }
}
