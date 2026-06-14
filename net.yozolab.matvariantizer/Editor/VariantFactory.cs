using UnityEditor;
using UnityEngine;

namespace YozoLab.MatVariantizer
{
    /// <summary>
    /// Material Variant アセットの生成・再利用と、生成先フォルダの作成を担う。
    /// 原本マテリアルには一切書き込まない。
    /// </summary>
    internal static class VariantFactory
    {
        /// <summary>
        /// 原本を親に持つ Material Variant を生成、または既存の同一 Variant を再利用して返す。
        /// </summary>
        internal static Material GetOrCreateVariant(Material original, string variantPath, out bool created)
        {
            created = false;
            if (original == null) return null;

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(variantPath);
            if (existing != null && existing.parent == original)
                return existing;

            EnsureFolder(ParentFolder(variantPath));

            // new Material(original) で見た目を完全一致させた上で parent を設定し、
            // 以後の編集差分を Variant 側に閉じ込める。原本は不変。
            var variant = new Material(original) { parent = original };
            AssetDatabase.CreateAsset(variant, variantPath);
            created = true;
            return variant;
        }

        /// <summary>Assets 配下のフォルダを再帰的に作成する（存在すれば何もしない）。</summary>
        internal static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;

            string parent = ParentFolder(folder);
            string leaf = folder.Substring(folder.LastIndexOf('/') + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static string ParentFolder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            int i = path.LastIndexOf('/');
            return i > 0 ? path.Substring(0, i) : path;
        }
    }
}
