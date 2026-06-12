using System.Collections.Generic;

namespace YozoLab.MeshBaker
{
    /// <summary>
    /// マテリアルモード1つ分の定義。
    /// 統合マテリアルに使うシェーダー、アトラス対象のテクスチャプロパティ、
    /// マップ割り当て時に有効化するシェーダーキーワードの対応表を持つ。
    /// </summary>
    internal class MaterialModeProfile
    {
        /// <summary>統合マテリアルに強制するシェーダー名（Shader.Find用。前方一致でバリアントを許容）</summary>
        public string shaderName;

        /// <summary>アトラス対象のテクスチャプロパティ（先頭がメイン＝レイアウトの基準）</summary>
        public string[] properties;

        /// <summary>テクスチャプロパティ → マップを有効化するシェーダーキーワード</summary>
        public Dictionary<string, string> keywords;

        /// <summary>発光マップのプロパティ（GI寄与設定の対象。無ければnull）</summary>
        public string emissionProperty;
    }

    /// <summary>
    /// マテリアルモード（Editor上の抽象化）ごとのプロファイル定義。
    /// Autodesk Interactive / Filamented / Mochie はいずれもUnity Standardと
    /// 同系のプロパティ名・キーワードを使う。
    /// </summary>
    internal static class MaterialModeProfiles
    {
        // --- Unity Standard系の共通テクスチャプロパティ ---
        private const string Albedo = "_MainTex";
        private const string Metallic = "_MetallicGlossMap";
        private const string SpecGloss = "_SpecGlossMap";
        private const string Normal = "_BumpMap";
        private const string Occlusion = "_OcclusionMap";
        private const string Emission = "_EmissionMap";
        private const string Parallax = "_ParallaxMap";
        private const string DetailAlbedo = "_DetailAlbedoMap";
        private const string DetailNormal = "_DetailNormalMap";

        /// <summary>
        /// テクスチャプロパティ → マップを有効化するシェーダーキーワード（Standard系共通）。
        /// _MainTex と _OcclusionMap はキーワード不要（割り当てるだけで反映される）。
        /// </summary>
        private static readonly Dictionary<string, string> StandardKeywords =
            new Dictionary<string, string>
            {
                { Metallic, "_METALLICGLOSSMAP" },
                { SpecGloss, "_SPECGLOSSMAP" },
                { Normal, "_NORMALMAP" },
                { Emission, "_EMISSION" },
                { Parallax, "_PARALLAXMAP" },
                { DetailAlbedo, "_DETAIL_MULX2" },
                { DetailNormal, "_DETAIL_MULX2" },
            };

        private static readonly string[] StandardProperties =
        {
            Albedo, Metallic, SpecGloss, Normal, Occlusion, Emission,
            Parallax, DetailAlbedo, DetailNormal,
        };

        /// <summary>Unity Standard（標準）。Specular setupバリアントも前方一致で維持される。</summary>
        private static readonly MaterialModeProfile UnityStandardProfile = new MaterialModeProfile
        {
            shaderName = "Standard",
            properties = StandardProperties,
            keywords = StandardKeywords,
            emissionProperty = Emission,
        };

        private static readonly MaterialModeProfile AutodeskInteractiveProfile = new MaterialModeProfile
        {
            shaderName = "Autodesk Interactive",
            properties = new[] { Albedo, Metallic, SpecGloss, Normal, Occlusion, Emission },
            keywords = StandardKeywords,
            emissionProperty = Emission,
        };

        /// <summary>Filamented（Silent/Filamented）。Unity Standard互換のプロパティ名・キーワード。</summary>
        private static readonly MaterialModeProfile FilamentedProfile = new MaterialModeProfile
        {
            shaderName = "Silent/Filamented",
            properties = StandardProperties,
            keywords = StandardKeywords,
            emissionProperty = Emission,
        };

        /// <summary>Mochie（Mochie/Standard）。Standard互換プロパティ＋パックドマップ。</summary>
        private static readonly MaterialModeProfile MochieProfile = new MaterialModeProfile
        {
            shaderName = "Mochie/Standard",
            properties = new[]
            {
                Albedo, Metallic, SpecGloss, Normal, Occlusion, Emission,
                Parallax, DetailAlbedo, DetailNormal, "_PackedMap",
            },
            keywords = StandardKeywords,
            emissionProperty = Emission,
        };

        private static readonly string[] DefaultProperties = { Albedo };

        /// <summary>モードに対応するプロファイル。Custom（手動指定）はnull。</summary>
        public static MaterialModeProfile GetProfile(MaterialMode mode)
        {
            switch (mode)
            {
                case MaterialMode.UnityStandard: return UnityStandardProfile;
                case MaterialMode.AutodeskInteractive: return AutodeskInteractiveProfile;
                case MaterialMode.Filamented: return FilamentedProfile;
                case MaterialMode.Mochie: return MochieProfile;
                default: return null;
            }
        }

        /// <summary>
        /// アトラス対象のテクスチャプロパティ一覧を返す（最低1つは保証）。
        /// </summary>
        public static string[] GetCollectProperties(MaterialMode mode, IList<string> customProperties)
        {
            MaterialModeProfile profile = GetProfile(mode);
            if (profile != null) return profile.properties;

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
