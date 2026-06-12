using System.Collections.Generic;

namespace YozoLab.MeshBaker
{
    /// <summary>
    /// マテリアルモード（Editor上の抽象化）ごとの、アトラス対象テクスチャプロパティの定義と
    /// シェーダー移行用のプロパティ対応表。
    /// </summary>
    internal static class MaterialModeProfiles
    {
        // --- Autodesk Interactive シェーダーのテクスチャプロパティ ---
        // 実際のプロパティ名はUnity Standardと同じ（_BumpMap / _OcclusionMap など）。
        public const string AI_Albedo = "_MainTex";          // Albedo (Base Color)
        public const string AI_Metallic = "_MetallicGlossMap"; // Metallic
        public const string AI_Roughness = "_SpecGlossMap";  // Roughness Map
        public const string AI_Normal = "_BumpMap";          // Normal Map
        public const string AI_Occlusion = "_OcclusionMap";  // Occlusion
        public const string AI_Emission = "_EmissionMap";    // Emission

        public static readonly string[] AutodeskInteractiveProperties =
        {
            AI_Albedo, AI_Metallic, AI_Roughness, AI_Normal, AI_Occlusion, AI_Emission,
        };

        public const string AutodeskInteractiveShaderName = "Autodesk Interactive";

        /// <summary>
        /// テクスチャプロパティ → マップを有効化するシェーダーキーワード（Autodesk Interactive/Standard共通）。
        /// _MainTex と _OcclusionMap はキーワード不要（割り当てるだけで反映される）。
        /// </summary>
        public static readonly Dictionary<string, string> AutodeskInteractiveKeywords =
            new Dictionary<string, string>
            {
                { AI_Metallic, "_METALLICGLOSSMAP" },
                { AI_Roughness, "_SPECGLOSSMAP" },
                { AI_Normal, "_NORMALMAP" },
                { AI_Emission, "_EMISSION" },
            };

        private static readonly string[] DefaultProperties = { "_MainTex" };

        /// <summary>
        /// アトラス対象のテクスチャプロパティ一覧を返す（最低1つは保証）。
        /// </summary>
        public static string[] GetCollectProperties(MaterialMode mode, IList<string> customProperties)
        {
            switch (mode)
            {
                case MaterialMode.AutodeskInteractive:
                    return AutodeskInteractiveProperties;
                default:
                    if (customProperties != null && customProperties.Count > 0)
                    {
                        var list = new List<string>();
                        foreach (string p in customProperties)
                        {
                            if (!string.IsNullOrEmpty(p)) list.Add(p);
                        }
                        if (list.Count > 0) return list.ToArray();
                    }
                    return DefaultProperties;
            }
        }
    }
}
