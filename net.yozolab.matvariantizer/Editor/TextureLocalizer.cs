using UnityEditor;
using UnityEngine;

namespace YozoLab.MatVariantizer
{
    /// <summary>
    /// テクスチャを複製してアバター別フォルダに隔離する。
    /// CopyAsset を使うことでインポート設定（圧縮 / sRGB / NormalMap 等）を保持する。
    /// 原本テクスチャには一切書き込まない。
    /// </summary>
    internal static class TextureLocalizer
    {
        /// <summary>
        /// 原本テクスチャのローカルコピーを生成、または既存コピーを再利用して返す。
        /// </summary>
        internal static Texture GetOrCreateCopy(Texture original, string copyPath, out bool created)
        {
            created = false;
            if (original == null || string.IsNullOrEmpty(copyPath)) return null;

            Texture existing = AssetDatabase.LoadAssetAtPath<Texture>(copyPath);
            if (existing != null) return existing;

            string srcPath = AssetDatabase.GetAssetPath(original);
            if (string.IsNullOrEmpty(srcPath)) return null;

            VariantFactory.EnsureFolder(ParentFolder(copyPath));
            if (!AssetDatabase.CopyAsset(srcPath, copyPath)) return null;

            created = true;
            return AssetDatabase.LoadAssetAtPath<Texture>(copyPath);
        }

        private static string ParentFolder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            int i = path.LastIndexOf('/');
            return i > 0 ? path.Substring(0, i) : path;
        }
    }
}
