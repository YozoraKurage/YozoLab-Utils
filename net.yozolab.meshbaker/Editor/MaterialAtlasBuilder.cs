using System;
using System.Collections.Generic;
using UnityEngine;

namespace YozoLab.MeshBaker
{
    /// <summary>
    /// 複数マテリアルのテクスチャを1枚のアトラスに統合する。
    /// 先頭のテクスチャプロパティ（通常 _MainTex）でレイアウトを決め、
    /// 残りのプロパティも同じレイアウトでアトラス化する。
    /// 非Readableや圧縮済みのテクスチャもRenderTexture経由のコピーで扱える。
    /// </summary>
    internal static class MaterialAtlasBuilder
    {
        internal class Result
        {
            /// <summary>マテリアルごとのアトラス内Rect（UV空間）</summary>
            public Rect[] rects;
            /// <summary>プロパティ名 → アトラステクスチャ（未保存のインメモリ）</summary>
            public readonly Dictionary<string, Texture2D> atlases = new Dictionary<string, Texture2D>();
        }

        /// <param name="uvBounds">各マテリアルが実際に使用しているUV範囲（この範囲を切り出してアトラスに詰める）</param>
        /// <param name="resolutionScales">各マテリアルのメインテクスチャ解像度倍率（非破壊の上書き。1で原寸）</param>
        internal static Result Build(
            IReadOnlyList<Material> materials, IReadOnlyList<Rect> uvBounds,
            IReadOnlyList<float> resolutionScales,
            IReadOnlyList<string> properties, int atlasSize, int padding,
            List<string> warnings)
        {
            string mainProperty = properties[0];
            var result = new Result();

            // 基準プロパティの読み取り可能コピーを作り、PackTexturesでレイアウトを決める
            var copies = new Texture2D[materials.Count];
            try
            {
                for (int i = 0; i < materials.Count; i++)
                {
                    copies[i] = CreateRegionCopy(
                        materials[i], mainProperty, uvBounds[i], atlasSize,
                        fallbackToColor: true, resolutionScale: resolutionScales[i],
                        sizeReferenceProperties: properties);
                }

                var mainAtlas = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                result.rects = mainAtlas.PackTextures(copies, padding, atlasSize, false);
                if (result.rects == null)
                {
                    throw new InvalidOperationException("テクスチャのアトラス化に失敗しました。アトラスサイズを大きくしてください。");
                }
                result.atlases[mainProperty] = mainAtlas;

                // 残りのプロパティは同じレイアウトで詰める
                for (int p = 1; p < properties.Count; p++)
                {
                    result.atlases[properties[p]] = BuildSecondaryAtlas(
                        materials, uvBounds, properties[p], result.rects,
                        mainAtlas.width, mainAtlas.height, atlasSize, warnings);
                }
            }
            finally
            {
                foreach (Texture2D copy in copies)
                {
                    if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
                }
            }

            return result;
        }

        private static Texture2D BuildSecondaryAtlas(
            IReadOnlyList<Material> materials, IReadOnlyList<Rect> uvBounds,
            string property, Rect[] rects, int atlasWidth, int atlasHeight, int atlasSize,
            List<string> warnings)
        {
            bool isNormal = IsNormalProperty(property);
            bool linear = IsLinearProperty(property);
            var atlas = new Texture2D(atlasWidth, atlasHeight, TextureFormat.RGBA32, false, linear: linear);

            // 既定値で埋める（ノーマル: 平坦な法線 / その他: 黒）
            Color32 fill = isNormal ? new Color32(128, 128, 255, 255) : new Color32(0, 0, 0, 255);
            var fillPixels = new Color32[atlasWidth * atlasHeight];
            for (int i = 0; i < fillPixels.Length; i++) fillPixels[i] = fill;
            atlas.SetPixels32(fillPixels);

            for (int i = 0; i < materials.Count; i++)
            {
                Material material = materials[i];
                if (material == null || !material.HasProperty(property)) continue;
                if (!(material.GetTexture(property) is Texture texture) || texture == null) continue;

                int x = Mathf.RoundToInt(rects[i].x * atlasWidth);
                int y = Mathf.RoundToInt(rects[i].y * atlasHeight);
                int w = Mathf.Max(1, Mathf.RoundToInt(rects[i].width * atlasWidth));
                int h = Mathf.Max(1, Mathf.RoundToInt(rects[i].height * atlasHeight));

                Texture2D copy = CopyTextureRegion(texture, uvBounds[i], w, h, linear, isNormal);
                try
                {
                    atlas.SetPixels32(x, y, w, h, copy.GetPixels32());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(copy);
                }
            }

            atlas.Apply();
            return atlas;
        }

        /// <summary>
        /// マテリアルの指定プロパティのテクスチャから、UV範囲boundsを切り出した読み取り可能コピーを作る。
        /// テクスチャがない場合はマテリアル色の単色テクスチャを返す。
        /// その場合でも他のアトラス対象マップ（ノーマル等）があれば、その解像度を確保できる
        /// サイズの単色を作り、レイアウト上の領域が潰れないようにする。
        /// </summary>
        private static Texture2D CreateRegionCopy(
            Material material, string property, Rect bounds, int atlasSize,
            bool fallbackToColor, float resolutionScale = 1f,
            IReadOnlyList<string> sizeReferenceProperties = null)
        {
            Texture texture = (material != null && material.HasProperty(property))
                ? material.GetTexture(property)
                : null;

            if (texture == null)
            {
                Color color = (fallbackToColor && material != null && material.HasProperty("_Color"))
                    ? material.color
                    : Color.white;

                int width = 4;
                int height = 4;
                Texture sizeReference = FindLargestTexture(material, sizeReferenceProperties);
                if (sizeReference != null)
                {
                    float scale = Mathf.Clamp(resolutionScale, 0.01f, 1f);
                    width = Mathf.Clamp(
                        Mathf.RoundToInt(sizeReference.width * bounds.width * scale), 4, atlasSize);
                    height = Mathf.Clamp(
                        Mathf.RoundToInt(sizeReference.height * bounds.height * scale), 4, atlasSize);
                }

                var solid = new Texture2D(width, height, TextureFormat.RGBA32, false);
                var pixels = new Color[width * height];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
                solid.SetPixels(pixels);
                solid.Apply();
                return solid;
            }

            float scale = Mathf.Clamp(resolutionScale, 0.01f, 1f);
            int width = Mathf.Clamp(Mathf.RoundToInt(texture.width * bounds.width * scale), 4, atlasSize);
            int height = Mathf.Clamp(Mathf.RoundToInt(texture.height * bounds.height * scale), 4, atlasSize);
            return CopyTextureRegion(texture, bounds, width, height,
                IsLinearProperty(property), IsNormalProperty(property));
        }

        /// <summary>指定プロパティ群の中で最大のテクスチャを返す（無ければnull）</summary>
        private static Texture FindLargestTexture(Material material, IReadOnlyList<string> properties)
        {
            if (material == null || properties == null) return null;
            Texture largest = null;
            foreach (string property in properties)
            {
                Texture candidate = material.HasProperty(property) ? material.GetTexture(property) : null;
                if (candidate == null) continue;
                if (largest == null ||
                    (long)candidate.width * candidate.height > (long)largest.width * largest.height)
                {
                    largest = candidate;
                }
            }
            return largest;
        }

        /// <summary>
        /// テクスチャのUV範囲boundsを width x height の読み取り可能なTexture2Dへコピーする。
        /// 範囲が[0,1]を超える場合はテクスチャのWrapMode（通常Repeat）に従ってタイリングされた状態で焼き込まれる。
        /// </summary>
        /// <param name="linear">リニア空間で扱う（ノーマル・データマップ）</param>
        /// <param name="decodeNormal">圧縮ノーマルのスウィズルをRGBノーマルにデコードする</param>
        internal static Texture2D CopyTextureRegion(
            Texture texture, Rect bounds, int width, int height, bool linear, bool decodeNormal)
        {
            RenderTexture rt = RenderTexture.GetTemporary(
                width, height, 0, RenderTextureFormat.ARGB32,
                linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(texture, rt, bounds.size, bounds.position);
                RenderTexture.active = rt;
                var copy = new Texture2D(width, height, TextureFormat.RGBA32, false, linear: linear);
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                copy.Apply();

                if (decodeNormal)
                {
                    DecodeNormalPixels(copy, texture);
                }
                return copy;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>
        /// 圧縮形式に応じたノーマルマップのスウィズル（DXT5nm/BC7はAG、BC5はRG）を
        /// 通常のRGBノーマル表現にデコードする。PNGとして保存しNormalMapとして再インポートするための処理。
        /// </summary>
        private static void DecodeNormalPixels(Texture2D copy, Texture source)
        {
            TextureFormat format = (source as Texture2D)?.format ?? TextureFormat.RGBA32;
            bool agSwizzle = format == TextureFormat.DXT5 || format == TextureFormat.BC7;
            bool rgOnly = format == TextureFormat.BC5;
            if (!agSwizzle && !rgOnly) return; // 非圧縮などはそのままRGBノーマルとして扱う

            Color[] pixels = copy.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                float nx = (agSwizzle ? pixels[i].a : pixels[i].r) * 2f - 1f;
                float ny = pixels[i].g * 2f - 1f;
                float nz = Mathf.Sqrt(Mathf.Max(0f, 1f - nx * nx - ny * ny));
                pixels[i] = new Color(nx * 0.5f + 0.5f, ny * 0.5f + 0.5f, nz * 0.5f + 0.5f, 1f);
            }
            copy.SetPixels(pixels);
            copy.Apply();
        }

        internal static bool IsNormalProperty(string property)
        {
            return property.IndexOf("Bump", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   property.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// リニア空間として扱うべきプロパティか（ノーマル・メタリック・粗さ・AO・マスクなどのデータマップ）。
        /// sRGBではなくリニアでサンプリング・保存する。
        /// </summary>
        internal static bool IsLinearProperty(string property)
        {
            if (IsNormalProperty(property)) return true;
            string p = property.ToLowerInvariant();
            return p.Contains("metallic") || p.Contains("rough") || p.Contains("smooth") ||
                   p.Contains("gloss") || p.Contains("spec") || p.Contains("occlusion") ||
                   p.Contains("aomap") || p.Contains("_ao") || p.Contains("mask") ||
                   p.Contains("height") || p.Contains("parallax");
        }
    }
}
