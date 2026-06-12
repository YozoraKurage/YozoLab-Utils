using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YozoLab.MeshBaker
{
    /// <summary>
    /// UVアイランド単位のアトラス化。
    ///
    /// アイランド分解は TexTransCore (https://github.com/ReinaS-64892/TexTransCore) の
    /// IslandUtility.UVtoIsland を参照して実装している
    /// (MIT License, Copyright (c) 2023 Reina_Sakiria):
    /// 同一UV座標の頂点をUnion-Findで結合する方式。
    /// パッキング本体（NFDH Plus FC + 充填率の拡大探索）は NfdhRectPacker に共通化されている。
    ///
    /// さらに、同一マテリアル内でソースUV上重なり合うアイランド
    /// （ミラーリングやスタックなど意図的に重複させたUV、複製メッシュの同一UV）を検知して
    /// 1つのパッキング単位にまとめ、アトラス領域を共有させる。
    /// 重複分のテクスチャがアトラスに複製されなくなるため、その分ほかのアイランドへ
    /// テクセル密度を再配分できる。
    /// </summary>
    internal static class UVIslandAtlasPacker
    {
        /// <summary>重なり面積が小さい方のアイランドのこの割合以上なら同一ユニットにまとめる</summary>
        private const float OverlapMergeThreshold = 0.5f;

        /// <summary>1つのBakePart内で連結したUVアイランド（検出単位）</summary>
        private class Island
        {
            public BakePart part;
            public readonly List<int> vertices = new List<int>();
            public Vector2 srcMin = new Vector2(float.MaxValue, float.MaxValue);
            public Vector2 srcMax = new Vector2(float.MinValue, float.MinValue);

            public float BBoxArea => Mathf.Max(srcMax.x - srcMin.x, 0f) * Mathf.Max(srcMax.y - srcMin.y, 0f);
        }

        /// <summary>
        /// パッキング単位。ソースUV上で重なるアイランド群は1つのユニットにまとまり、
        /// 相対位置を保ったままアトラスの同じ領域を共有する。
        /// </summary>
        private class PackUnit : NfdhRectPacker.Item
        {
            public BakeMaterialGroup group;
            public readonly List<Island> islands = new List<Island>();
            public Vector2 srcMin = new Vector2(float.MaxValue, float.MaxValue);
            public Vector2 srcMax = new Vector2(float.MinValue, float.MinValue);

            /// <summary>元テクスチャ上での占有ピクセルサイズ（テクセル密度の基準）</summary>
            public Vector2 basePxSize;

            public Vector2 SrcSize => Vector2.Max(srcMax - srcMin, new Vector2(1e-6f, 1e-6f));

            public void Add(Island island)
            {
                islands.Add(island);
                srcMin = Vector2.Min(srcMin, island.srcMin);
                srcMax = Vector2.Max(srcMax, island.srcMax);
            }
        }

        /// <summary>
        /// 全グループのUVアイランドを検出して1枚のアトラスに詰め、
        /// 各PartのUVをアトラス空間へ書き換えた上でアトラステクスチャ群を返す。
        /// </summary>
        internal static MaterialAtlasBuilder.Result Pack(
            MeshBakeAssembly assembly,
            List<BakeMaterialGroup> groups, IReadOnlyList<string> properties,
            int atlasSize, int paddingPx, List<string> warnings, List<string> infos)
        {
            string mainProperty = properties[0];
            var units = new List<PackUnit>();
            int totalIslands = 0;
            foreach (BakeMaterialGroup group in groups)
            {
                Texture mainTexture = GetMainTexture(group.material, mainProperty);
                Vector2 textureSize = mainTexture != null
                    ? new Vector2(mainTexture.width, mainTexture.height)
                    : new Vector2(64, 64);
                // テクスチャ解像度の上書き（非破壊）をテクセル密度に反映する
                int longestEdge = Mathf.RoundToInt(Mathf.Max(textureSize.x, textureSize.y));
                float resolutionScale = assembly.GetResolutionScale(mainTexture, longestEdge);

                var groupIslands = new List<Island>();
                foreach (BakePart part in group.parts)
                {
                    groupIslands.AddRange(DetectIslands(part));
                }
                totalIslands += groupIslands.Count;

                foreach (PackUnit unit in BuildPackUnits(groupIslands, assembly.mergeOverlappingUVIslands))
                {
                    unit.group = group;
                    unit.basePxSize = Vector2.Max(
                        Vector2.Scale(unit.SrcSize, textureSize) * resolutionScale,
                        new Vector2(2f, 2f));
                    unit.baseSize = unit.basePxSize / atlasSize;
                    units.Add(unit);
                }
            }
            if (units.Count == 0)
            {
                throw new InvalidOperationException("UVアイランドが見つかりませんでした。");
            }
            if (totalIslands > units.Count)
            {
                infos.Add($"UV重複の最適化: 重なり合う{totalIslands}アイランドを{units.Count}ユニットに統合し、" +
                          "アトラス領域を共有しました。");
            }

            float padding = Mathf.Max(paddingPx, 1) / (float)atlasSize;
            float scale = NfdhRectPacker.PackWithScaleSearch(units, padding);
            if (scale <= 0f)
            {
                throw new InvalidOperationException(
                    "UVアイランドをアトラスに収められませんでした。アトラスサイズを大きくするか、Paddingを小さくしてください。");
            }
            if (scale < 0.7f)
            {
                warnings.Add($"アトラス収容のため全体のテクセル密度が約{scale:P0}に縮小されました。" +
                             "解像度を保ちたい場合はアトラスサイズを大きくしてください。");
            }

            RemapUVs(units);

            var result = new MaterialAtlasBuilder.Result();
            foreach (string property in properties)
            {
                result.atlases[property] = RenderAtlas(
                    units, property, property == mainProperty, atlasSize, paddingPx);
            }
            return result;
        }

        // ---------------------------------------------------------------
        // アイランド検出（Union-Find: TexTransCore IslandUtility方式）
        // ---------------------------------------------------------------

        private static List<Island> DetectIslands(BakePart part)
        {
            Vector2[] uv = part.uv;
            int[] indices = part.indices;

            // 同一UV座標に共通インデックスを割り当てる
            var uvToUnique = new Dictionary<Vector2, int>(uv.Length);
            var vertexToUnique = new int[uv.Length];
            int uniqueCount = 0;
            for (int i = 0; i < uv.Length; i++)
            {
                if (!uvToUnique.TryGetValue(uv[i], out int unique))
                {
                    unique = uniqueCount++;
                    uvToUnique.Add(uv[i], unique);
                }
                vertexToUnique[i] = unique;
            }

            // Union-Findで三角形を共有するUVを結合する
            var parent = new int[uniqueCount];
            for (int i = 0; i < uniqueCount; i++) parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = Find(vertexToUnique[indices[i]]);
                int b = Find(vertexToUnique[indices[i + 1]]);
                int c = Find(vertexToUnique[indices[i + 2]]);
                if (b != a) parent[b] = a;
                if (c != Find(a)) parent[Find(c)] = Find(a);
            }

            // 代表ごとにアイランドを作り、頂点を割り当てる
            var byRoot = new Dictionary<int, Island>();
            var islands = new List<Island>();
            for (int v = 0; v < uv.Length; v++)
            {
                int root = Find(vertexToUnique[v]);
                if (!byRoot.TryGetValue(root, out Island island))
                {
                    island = new Island { part = part };
                    byRoot.Add(root, island);
                    islands.Add(island);
                }
                island.vertices.Add(v);
                island.srcMin = Vector2.Min(island.srcMin, uv[v]);
                island.srcMax = Vector2.Max(island.srcMax, uv[v]);
            }
            return islands;
        }

        // ---------------------------------------------------------------
        // 重複アイランドの統合（同一マテリアル内のスタック/ミラー/複製の検知）
        // ---------------------------------------------------------------

        /// <summary>
        /// ソースUV上で十分に重なり合うアイランド同士をUnion-Findで連結し、
        /// パッキング単位（PackUnit）にまとめる。
        /// 重なったアイランドは同じテクスチャ領域をサンプリングしているため、
        /// アトラス上で1つの領域を共有しても描画結果は変わらない。
        /// </summary>
        private static List<PackUnit> BuildPackUnits(List<Island> islands, bool mergeOverlapping)
        {
            var parent = new int[islands.Count];
            for (int i = 0; i < islands.Count; i++) parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            if (mergeOverlapping)
            {
                for (int a = 0; a < islands.Count; a++)
                {
                    for (int b = a + 1; b < islands.Count; b++)
                    {
                        if (Find(a) == Find(b)) continue;
                        if (ShouldMerge(islands[a], islands[b]))
                        {
                            parent[Find(b)] = Find(a);
                        }
                    }
                }
            }

            var byRoot = new Dictionary<int, PackUnit>();
            var units = new List<PackUnit>();
            for (int i = 0; i < islands.Count; i++)
            {
                int root = Find(i);
                if (!byRoot.TryGetValue(root, out PackUnit unit))
                {
                    unit = new PackUnit();
                    byRoot.Add(root, unit);
                    units.Add(unit);
                }
                unit.Add(islands[i]);
            }
            return units;
        }

        /// <summary>
        /// バウンディングボックスの重なりが、小さい方の面積の一定割合以上なら統合する。
        /// 完全一致のスタックや包含は1.0となり確実に統合され、
        /// 隣接アイランドのわずかな接触は統合されない。
        /// </summary>
        private static bool ShouldMerge(Island a, Island b)
        {
            float w = Mathf.Min(a.srcMax.x, b.srcMax.x) - Mathf.Max(a.srcMin.x, b.srcMin.x);
            float h = Mathf.Min(a.srcMax.y, b.srcMax.y) - Mathf.Max(a.srcMin.y, b.srcMin.y);
            if (w <= 0f || h <= 0f) return false;

            float minArea = Mathf.Min(a.BBoxArea, b.BBoxArea);
            if (minArea <= 1e-12f) return true; // 点・線状の退化アイランドは重なっていれば吸収する
            return w * h / minArea >= OverlapMergeThreshold;
        }

        // ---------------------------------------------------------------
        // UV書き換えとアトラス描画
        // ---------------------------------------------------------------

        private static void RemapUVs(List<PackUnit> units)
        {
            foreach (PackUnit unit in units)
            {
                Vector2 srcSize = unit.SrcSize;
                foreach (Island island in unit.islands)
                {
                    foreach (int v in island.vertices)
                    {
                        Vector2 uv = island.part.uv[v];
                        // ユニットのバウンディングボックス基準で写像することで、
                        // 統合されたアイランド同士の相対位置（重なり）が保たれる
                        float fu = (uv.x - unit.srcMin.x) / srcSize.x;
                        float fv = (uv.y - unit.srcMin.y) / srcSize.y;
                        island.part.uv[v] = unit.rotated
                            // 90度回転: 転置ピクセルコピーと整合する写像
                            ? unit.pos + new Vector2((1f - fv) * unit.size.x, fu * unit.size.y)
                            : unit.pos + new Vector2(fu * unit.size.x, fv * unit.size.y);
                    }
                }
            }
        }

        private static Texture2D RenderAtlas(
            List<PackUnit> units, string property, bool isMainProperty, int atlasSize, int paddingPx)
        {
            bool isNormal = MaterialAtlasBuilder.IsNormalProperty(property);
            bool linear = MaterialAtlasBuilder.IsLinearProperty(property);
            var atlas = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, false, linear: linear);

            Color32 fill = isNormal ? new Color32(128, 128, 255, 255) : new Color32(0, 0, 0, 255);
            var fillPixels = new Color32[atlasSize * atlasSize];
            for (int i = 0; i < fillPixels.Length; i++) fillPixels[i] = fill;
            atlas.SetPixels32(fillPixels);

            int bleed = Mathf.Max(1, paddingPx / 2);

            foreach (PackUnit unit in units)
            {
                Material material = unit.group.material;
                Texture texture = (material != null && material.HasProperty(property))
                    ? material.GetTexture(property)
                    : null;

                // アトラス上の描画先（にじみ対策で外周bleedピクセル分広げる）
                int dstX = Mathf.RoundToInt(unit.pos.x * atlasSize) - bleed;
                int dstY = Mathf.RoundToInt(unit.pos.y * atlasSize) - bleed;
                int dstW = Mathf.Max(1, Mathf.RoundToInt(unit.size.x * atlasSize)) + bleed * 2;
                int dstH = Mathf.Max(1, Mathf.RoundToInt(unit.size.y * atlasSize)) + bleed * 2;

                if (texture == null)
                {
                    if (!isMainProperty) continue; // 既定色のまま
                    Color color = material != null && material.HasProperty("_Color") ? material.color : Color.white;
                    FillRegion(atlas, dstX, dstY, dstW, dstH, color);
                    continue;
                }

                // ソース側の切り出し範囲（dst拡張と同じ比率で広げる）
                Vector2 srcSize = unit.SrcSize;
                int innerW = dstW - bleed * 2;
                int innerH = dstH - bleed * 2;
                float bleedFracX = bleed / (float)innerW;
                float bleedFracY = bleed / (float)innerH;
                // 回転時はdstのX軸がソースのV軸に対応する
                float expandU = (unit.rotated ? bleedFracY : bleedFracX) * srcSize.x;
                float expandV = (unit.rotated ? bleedFracX : bleedFracY) * srcSize.y;
                var srcRect = new Rect(
                    unit.srcMin.x - expandU, unit.srcMin.y - expandV,
                    srcSize.x + expandU * 2f, srcSize.y + expandV * 2f);

                // 回転時は転置するため、コピーは幅と高さを入れ替えて取得する
                int copyW = unit.rotated ? dstH : dstW;
                int copyH = unit.rotated ? dstW : dstH;
                Texture2D copy = MaterialAtlasBuilder.CopyTextureRegion(texture, srcRect, copyW, copyH, linear, isNormal);
                try
                {
                    Color32[] pixels = copy.GetPixels32();
                    if (unit.rotated) pixels = Transpose(pixels, copyW, copyH);
                    WriteRegionClipped(atlas, dstX, dstY, dstW, dstH, pixels);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(copy);
                }
            }

            atlas.Apply();
            return atlas;
        }

        /// <summary>90度回転（反時計回り）: P(w x h) → Q(h x w), Q[x, h-1-y] = P[x, y]</summary>
        private static Color32[] Transpose(Color32[] pixels, int width, int height)
        {
            var rotated = new Color32[pixels.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    rotated[x * height + (height - 1 - y)] = pixels[y * width + x];
                }
            }
            return rotated;
        }

        private static void FillRegion(Texture2D atlas, int x, int y, int width, int height, Color color)
        {
            var pixels = new Color32[width * height];
            Color32 c = color;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
            WriteRegionClipped(atlas, x, y, width, height, pixels);
        }

        /// <summary>アトラス範囲外にはみ出す部分を切り捨てつつ書き込む</summary>
        private static void WriteRegionClipped(Texture2D atlas, int x, int y, int width, int height, Color32[] pixels)
        {
            int clipX = Mathf.Max(0, -x);
            int clipY = Mathf.Max(0, -y);
            int writeW = Mathf.Min(width - clipX, atlas.width - Mathf.Max(0, x));
            int writeH = Mathf.Min(height - clipY, atlas.height - Mathf.Max(0, y));
            if (writeW <= 0 || writeH <= 0) return;

            if (clipX == 0 && clipY == 0 && writeW == width && writeH == height)
            {
                atlas.SetPixels32(x, y, width, height, pixels);
                return;
            }

            var clipped = new Color32[writeW * writeH];
            for (int row = 0; row < writeH; row++)
            {
                Array.Copy(pixels, (row + clipY) * width + clipX, clipped, row * writeW, writeW);
            }
            atlas.SetPixels32(Mathf.Max(0, x), Mathf.Max(0, y), writeW, writeH, clipped);
        }

        private static Texture GetMainTexture(Material material, string property)
        {
            return (material != null && material.HasProperty(property))
                ? material.GetTexture(property)
                : null;
        }
    }
}
