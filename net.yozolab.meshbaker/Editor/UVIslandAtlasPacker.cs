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

        /// <summary>テクスチャを持たないマテリアルに割り当てる単色スウォッチの一辺(px)</summary>
        private const float SwatchPx = 8f;

        /// <summary>1つのBakePart内で連結したUVアイランド（検出単位）</summary>
        private class Island
        {
            public BakePart part;
            public readonly List<int> vertices = new List<int>();
            public Vector2 srcMin = new Vector2(float.MaxValue, float.MaxValue);
            public Vector2 srcMax = new Vector2(float.MinValue, float.MinValue);

            /// <summary>ワールド（アセンブリローカル）空間での三角形面積の合計（密度正規化用）</summary>
            public float worldArea;
            /// <summary>ソースUV空間での三角形面積の合計（密度正規化用）</summary>
            public float uvArea;

            public float BBoxArea => Mathf.Max(srcMax.x - srcMin.x, 0f) * Mathf.Max(srcMax.y - srcMin.y, 0f);
        }

        /// <summary>
        /// パッキング単位。ソースUV上で重なるアイランド群は1つのユニットにまとまり、
        /// 相対位置を保ったままアトラスの同じ領域を共有する。
        /// </summary>
        private class PackUnit : NfdhRectPacker.Item
        {
            public BakeMaterialGroup group;

            /// <summary>
            /// テクスチャなしマテリアルの単色スウォッチ。
            /// アイランド展開せず、グループ全パートのUVをこの領域の中心へ集約する。
            /// </summary>
            public bool collapsed;

            public readonly List<Island> islands = new List<Island>();
            public Vector2 srcMin = new Vector2(float.MaxValue, float.MaxValue);
            public Vector2 srcMax = new Vector2(float.MinValue, float.MinValue);

            /// <summary>目標とする占有ピクセルサイズ（密度正規化が有効なら正規化後の値）</summary>
            public Vector2 basePxSize;
            /// <summary>元テクスチャ上での原寸ピクセルサイズ（これを超えるアップスケールはしない）</summary>
            public Vector2 capPxSize;
            public float worldArea;
            public float uvArea;

            public Vector2 SrcSize => Vector2.Max(srcMax - srcMin, new Vector2(1e-6f, 1e-6f));

            public void Add(Island island)
            {
                islands.Add(island);
                srcMin = Vector2.Min(srcMin, island.srcMin);
                srcMax = Vector2.Max(srcMax, island.srcMax);
                worldArea += island.worldArea;
                uvArea += island.uvArea;
            }
        }

        /// <summary>
        /// 全グループのUVアイランドを検出して1枚のアトラスに詰め、
        /// 各PartのUVをアトラス空間へ書き換えた上でアトラステクスチャ群を返す。
        /// </summary>
        internal static MaterialAtlasBuilder.Result Pack(
            MeshBakeAssembly assembly,
            List<BakeMaterialGroup> groups, IReadOnlyList<string> properties,
            int atlasSize, List<string> warnings, List<string> infos)
        {
            string mainProperty = properties[0];
            var units = new List<PackUnit>();
            int totalIslands = 0;
            int collapsedGroups = 0;
            foreach (BakeMaterialGroup group in groups)
            {
                // どのアトラス対象プロパティにもテクスチャを持たないマテリアルは、
                // ユニークなテクセル領域が不要なのでアイランド展開せず、
                // 小さな単色スウォッチ1つに集約する（全UVがその中心を指す）。
                // 空いた領域は充填率探索でテクスチャ持ちマテリアルの密度へ再配分される。
                if (IsColorOnlyGroup(group.material, properties))
                {
                    var swatch = new PackUnit { group = group, collapsed = true };
                    swatch.srcMin = Vector2.zero;
                    swatch.srcMax = Vector2.one;
                    swatch.basePxSize = new Vector2(SwatchPx, SwatchPx);
                    swatch.capPxSize = swatch.basePxSize;
                    units.Add(swatch);
                    collapsedGroups++;
                    continue;
                }

                // テクセル密度の基準テクスチャ。メインが無いマテリアル（単色＋ノーマルのみ等）でも
                // 他のアトラス対象マップがあるなら、その中で最大のものを基準にして解像度を確保する
                Texture densityReference = GetMainTexture(group.material, mainProperty);
                if (densityReference == null)
                {
                    foreach (string property in properties)
                    {
                        Texture candidate = GetMainTexture(group.material, property);
                        if (candidate == null) continue;
                        if (densityReference == null ||
                            (long)candidate.width * candidate.height >
                            (long)densityReference.width * densityReference.height)
                        {
                            densityReference = candidate;
                        }
                    }
                }
                Vector2 textureSize = densityReference != null
                    ? new Vector2(densityReference.width, densityReference.height)
                    : new Vector2(64, 64);
                // テクスチャ解像度の上書き（非破壊）をテクセル密度に反映する
                int longestEdge = Mathf.RoundToInt(Mathf.Max(textureSize.x, textureSize.y));
                float resolutionScale = assembly.GetResolutionScale(densityReference, longestEdge);

                var groupIslands = new List<Island>();
                foreach (BakePart part in group.parts)
                {
                    List<Island> islands = DetectIslands(part);
                    if (assembly.normalizeTexelDensity) AccumulateIslandAreas(part, islands);
                    groupIslands.AddRange(islands);
                }
                totalIslands += groupIslands.Count;

                foreach (PackUnit unit in BuildPackUnits(groupIslands, assembly.mergeOverlappingUVIslands))
                {
                    unit.group = group;
                    unit.basePxSize = Vector2.Max(
                        Vector2.Scale(unit.SrcSize, textureSize) * resolutionScale,
                        new Vector2(2f, 2f));
                    // 原寸（解像度上書き後）を超えるアップスケールはしない
                    unit.capPxSize = unit.basePxSize;
                    units.Add(unit);
                }
            }
            if (units.Count == 0)
            {
                throw new InvalidOperationException("UVアイランドが見つかりませんでした。");
            }
            int packedUnits = units.Count - collapsedGroups;
            if (totalIslands > packedUnits)
            {
                infos.Add($"UV重複の最適化: 重なり合う{totalIslands}アイランドを{packedUnits}ユニットに統合し、" +
                          "アトラス領域を共有しました。");
            }
            if (collapsedGroups > 0)
            {
                infos.Add($"テクスチャなしマテリアル{collapsedGroups}件を単色スウォッチ（{SwatchPx:0}px）に集約し、" +
                          "アトラス領域を節約しました。");
            }

            if (assembly.normalizeTexelDensity)
            {
                ApplyDensityNormalization(units);
            }

            int finalSize = atlasSize;
            if (NfdhPackAt(units, finalSize, assembly) <= 0f)
            {
                throw new InvalidOperationException(
                    "UVアイランドをアトラスに収められませんでした。アトラスサイズを大きくするか、Paddingを小さくしてください。");
            }

            // 目標密度のまま収まるなら、より小さいアトラスへ縮小して無駄を省く
            if (assembly.autoShrinkAtlas)
            {
                while (finalSize > 128 && AllAtTargetDensity(units, finalSize))
                {
                    int half = finalSize / 2;
                    if (NfdhPackAt(units, half, assembly) > 0f && AllAtTargetDensity(units, half))
                    {
                        finalSize = half;
                        continue;
                    }
                    break;
                }
                // 直前の縮小試行で失敗したレイアウトが残っている場合に備えて、採用サイズで確定パックする
                NfdhPackAt(units, finalSize, assembly);
                if (finalSize != atlasSize)
                {
                    infos.Add($"アトラスを{atlasSize}px→{finalSize}pxに自動縮小しました（テクセル密度の低下ほぼなし）。");
                }
            }

            float minDensity = MinAchievedDensity(units, finalSize);
            if (minDensity < 0.7f)
            {
                warnings.Add($"アトラス収容のため一部のテクセル密度が約{minDensity:P0}まで縮小されました。" +
                             "解像度を保ちたい場合はアトラスサイズを大きくしてください。");
            }

            RemapUVs(units);

            int effectivePaddingPx = assembly.GetEffectiveAtlasPadding(finalSize);
            var result = new MaterialAtlasBuilder.Result();
            foreach (string property in properties)
            {
                result.atlases[property] = RenderAtlas(
                    units, property, property == mainProperty, finalSize, effectivePaddingPx);
            }
            return result;
        }

        /// <summary>指定アトラスサイズでの相対サイズ・上限・余白を設定してパックする</summary>
        private static float NfdhPackAt(List<PackUnit> units, int atlasSize, MeshBakeAssembly assembly)
        {
            int paddingPx = assembly.GetEffectiveAtlasPadding(atlasSize);
            float padding = Mathf.Max(paddingPx, 1) / (float)atlasSize;
            foreach (PackUnit unit in units)
            {
                unit.baseSize = unit.basePxSize / atlasSize;
                unit.maxSize = unit.capPxSize / atlasSize;
            }
            return NfdhRectPacker.PackWithScaleSearch(units, padding);
        }

        /// <summary>全ユニットが目標ピクセルサイズ（basePxSize）にほぼ達しているか（自動縮小の判定）</summary>
        private static bool AllAtTargetDensity(List<PackUnit> units, int atlasSize)
        {
            const float tolerance = 0.98f;
            foreach (PackUnit unit in units)
            {
                if (AchievedDensity(unit, atlasSize) < tolerance) return false;
            }
            return true;
        }

        private static float MinAchievedDensity(List<PackUnit> units, int atlasSize)
        {
            float min = float.MaxValue;
            foreach (PackUnit unit in units)
            {
                min = Mathf.Min(min, AchievedDensity(unit, atlasSize));
            }
            return min;
        }

        /// <summary>目標ピクセルサイズに対する実配置サイズの比（90度回転を考慮して長辺同士で比較）</summary>
        private static float AchievedDensity(PackUnit unit, int atlasSize)
        {
            float placedLongest = Mathf.Max(unit.size.x, unit.size.y) * atlasSize;
            float targetLongest = Mathf.Max(unit.basePxSize.x, unit.basePxSize.y);
            return placedLongest / Mathf.Max(targetLongest, 1e-6f);
        }

        /// <summary>
        /// テクセル密度の正規化: 各ユニットの目標ピクセルサイズを
        /// ワールド表面積に比例した密度（UVサイズ × sqrt(ワールド面積/UV面積)）へ置き換える。
        /// 全体の合計面積は元と同等に保ち、原寸（capPxSize）は超えない。
        /// </summary>
        private static void ApplyDensityNormalization(List<PackUnit> units)
        {
            var normSizes = new Vector2[units.Count];
            double originalArea = 0;
            double normalizedArea = 0;
            for (int i = 0; i < units.Count; i++)
            {
                PackUnit unit = units[i];
                if (unit.worldArea <= 1e-12f || unit.uvArea <= 1e-12f) continue; // 退化は原寸のまま
                normSizes[i] = unit.SrcSize * Mathf.Sqrt(unit.worldArea / unit.uvArea);
                originalArea += (double)unit.basePxSize.x * unit.basePxSize.y;
                normalizedArea += (double)normSizes[i].x * normSizes[i].y;
            }
            if (normalizedArea <= 0) return;

            // 合計ピクセル面積を保存するスケール（均一密度のときのピクセル/ワールド比）
            float c = Mathf.Sqrt((float)(originalArea / normalizedArea));
            for (int i = 0; i < units.Count; i++)
            {
                if (normSizes[i] == Vector2.zero) continue;
                PackUnit unit = units[i];
                Vector2 px = normSizes[i] * c;
                // 原寸を超える分は原寸に抑える（アスペクト比は共通なので一様係数でよい）
                float cap = Mathf.Min(1f, Mathf.Min(
                    unit.capPxSize.x / Mathf.Max(px.x, 1e-6f),
                    unit.capPxSize.y / Mathf.Max(px.y, 1e-6f)));
                unit.basePxSize = Vector2.Max(px * cap, new Vector2(2f, 2f));
            }
        }

        /// <summary>パートの三角形を走査して、各アイランドのワールド面積とUV面積を集計する</summary>
        private static void AccumulateIslandAreas(BakePart part, List<Island> islands)
        {
            var vertexToIsland = new Island[part.uv.Length];
            foreach (Island island in islands)
            {
                foreach (int v in island.vertices) vertexToIsland[v] = island;
            }

            for (int i = 0; i < part.indices.Length; i += 3)
            {
                int a = part.indices[i];
                int b = part.indices[i + 1];
                int c = part.indices[i + 2];
                Island island = vertexToIsland[a]; // 三角形の3頂点は同一アイランドに属する
                if (island == null) continue;

                island.worldArea += Vector3.Cross(
                    part.positions[b] - part.positions[a],
                    part.positions[c] - part.positions[a]).magnitude * 0.5f;

                Vector2 e1 = part.uv[b] - part.uv[a];
                Vector2 e2 = part.uv[c] - part.uv[a];
                island.uvArea += Mathf.Abs(e1.x * e2.y - e1.y * e2.x) * 0.5f;
            }
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
                if (unit.collapsed)
                {
                    // 単色スウォッチ: グループ全パートのUVを領域中心の1点へ集約する
                    // （どこをサンプリングしても同じ色なので、にじみに最も安全な中心を使う）
                    Vector2 center = unit.pos + unit.size * 0.5f;
                    foreach (BakePart part in unit.group.parts)
                    {
                        for (int i = 0; i < part.uv.Length; i++) part.uv[i] = center;
                    }
                    continue;
                }

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

        /// <summary>どのアトラス対象プロパティにもテクスチャを持たない（単色のみの）マテリアルか</summary>
        private static bool IsColorOnlyGroup(Material material, IReadOnlyList<string> properties)
        {
            foreach (string property in properties)
            {
                if (GetMainTexture(material, property) != null) return false;
            }
            return true;
        }
    }
}
