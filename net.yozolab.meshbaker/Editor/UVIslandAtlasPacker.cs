using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YozoLab.MeshBaker
{
    /// <summary>
    /// UVアイランド単位のアトラス化。
    ///
    /// アルゴリズムは TexTransCore (https://github.com/ReinaS-64892/TexTransCore) および
    /// TexTransTool (https://github.com/ReinaS-64892/TexTransTool) を参照して実装している
    /// (いずれも MIT License, Copyright (c) 2023 Reina_Sakiria):
    /// - UVアイランド分解: 同一UV座標の頂点をUnion-Findで結合する方式 (IslandUtility.UVtoIsland)
    /// - パッキング: NFDH Plus FC — 縦長アイランドを90度回転して高さ降順に整列し、
    ///   棚(シェルフ)の床側は左から、天井側は右から詰めるFloor-Ceiling法 (NFDHPlasFC)
    /// - 充填率最適化: 全体を面積比0.5相当へ縮小してから、収まらなくなる直前まで
    ///   拡大を繰り返す探索 (IslandRelocationManager.RelocateLoop)
    /// </summary>
    internal static class UVIslandAtlasPacker
    {
        private class Island
        {
            public BakeMaterialGroup group;
            public BakePart part;
            public readonly List<int> vertices = new List<int>();
            public Vector2 srcMin = new Vector2(float.MaxValue, float.MaxValue);
            public Vector2 srcMax = new Vector2(float.MinValue, float.MinValue);

            /// <summary>元テクスチャ上での占有ピクセルサイズ（テクセル密度の基準）</summary>
            public Vector2 basePxSize;

            // パッキング作業用と確定結果
            public Vector2 size;
            public Vector2 pos;
            public bool rotated;
            public Vector2 bestSize;
            public Vector2 bestPos;
            public bool bestRotated;

            public Vector2 SrcSize => Vector2.Max(srcMax - srcMin, new Vector2(1e-6f, 1e-6f));
        }

        /// <summary>
        /// 全グループのUVアイランドを検出して1枚のアトラスに詰め、
        /// 各PartのUVをアトラス空間へ書き換えた上でアトラステクスチャ群を返す。
        /// </summary>
        internal static MaterialAtlasBuilder.Result Pack(
            MeshBakeAssembly assembly,
            List<BakeMaterialGroup> groups, IReadOnlyList<string> properties,
            int atlasSize, int paddingPx, List<string> warnings)
        {
            string mainProperty = properties[0];
            var islands = new List<Island>();
            foreach (BakeMaterialGroup group in groups)
            {
                Texture mainTexture = GetMainTexture(group.material, mainProperty);
                Vector2 textureSize = mainTexture != null
                    ? new Vector2(mainTexture.width, mainTexture.height)
                    : new Vector2(64, 64);
                // テクスチャ解像度の上書き（非破壊）をテクセル密度に反映する
                int longestEdge = Mathf.RoundToInt(Mathf.Max(textureSize.x, textureSize.y));
                float resolutionScale = assembly.GetResolutionScale(mainTexture, longestEdge);
                foreach (BakePart part in group.parts)
                {
                    foreach (Island island in DetectIslands(part))
                    {
                        island.group = group;
                        island.basePxSize = Vector2.Max(
                            Vector2.Scale(island.SrcSize, textureSize) * resolutionScale,
                            new Vector2(2f, 2f));
                        islands.Add(island);
                    }
                }
            }
            if (islands.Count == 0)
            {
                throw new InvalidOperationException("UVアイランドが見つかりませんでした。");
            }

            float padding = Mathf.Max(paddingPx, 1) / (float)atlasSize;
            float scale = RelocateLoop(islands, atlasSize, padding);
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

            RemapUVs(islands);

            var result = new MaterialAtlasBuilder.Result();
            foreach (string property in properties)
            {
                result.atlases[property] = RenderAtlas(
                    islands, property, property == mainProperty, atlasSize, paddingPx);
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
        // パッキング（NFDH Plus Floor-Ceiling + 拡大探索）
        // ---------------------------------------------------------------

        /// <summary>面積比0.5相当から開始し、収まらなくなる直前までテクセル密度を拡大する</summary>
        private static float RelocateLoop(List<Island> islands, int atlasSize, float padding)
        {
            float areaSum = islands.Sum(i =>
                (i.basePxSize.x / atlasSize) * (i.basePxSize.y / atlasSize));

            for (float budget = 0.5f; budget > 0.02f; budget -= 0.05f)
            {
                float scale = Mathf.Sqrt(budget / Mathf.Max(areaSum, 1e-9f));
                if (!TryPackAtScale(islands, atlasSize, padding, scale)) continue;

                SaveLayout(islands);
                float best = scale;
                for (int step = 0; step < 64; step++)
                {
                    float trial = best * 1.02f;
                    if (!TryPackAtScale(islands, atlasSize, padding, trial)) break;
                    SaveLayout(islands);
                    best = trial;
                }
                RestoreLayout(islands);
                return best;
            }
            return -1f;
        }

        private static bool TryPackAtScale(List<Island> islands, int atlasSize, float padding, float scale)
        {
            float maxLength = 1f - padding * 2f - 0.001f;
            foreach (Island island in islands)
            {
                Vector2 size = island.basePxSize / atlasSize * scale;
                // 1つでも枠を超えるアイランドがあると全体が破綻するため、そのアイランドだけ縮める
                float longest = Mathf.Max(size.x, size.y);
                if (longest > maxLength) size *= maxLength / longest;
                island.size = size;
                island.rotated = false;
            }
            return TryPack(islands, padding);
        }

        private static void SaveLayout(List<Island> islands)
        {
            foreach (Island island in islands)
            {
                island.bestSize = island.size;
                island.bestPos = island.pos;
                island.bestRotated = island.rotated;
            }
        }

        private static void RestoreLayout(List<Island> islands)
        {
            foreach (Island island in islands)
            {
                island.size = island.bestSize;
                island.pos = island.bestPos;
                island.rotated = island.bestRotated;
            }
        }

        /// <summary>NFDH+FC: 高さ降順で、各棚の床(左から)と天井(右から)に詰める</summary>
        private static bool TryPack(List<Island> islands, float padding)
        {
            // 縦長のアイランドは90度回転して横長に揃える
            foreach (Island island in islands)
            {
                if (island.size.y > island.size.x)
                {
                    island.size = new Vector2(island.size.y, island.size.x);
                    island.rotated = true;
                }
            }

            List<Island> order = islands.OrderByDescending(i => i.size.y).ToList();
            var shelves = new List<Shelf>();

            foreach (Island island in order)
            {
                bool placed = false;
                foreach (Shelf shelf in shelves)
                {
                    if (shelf.TryPlace(island, padding)) { placed = true; break; }
                }
                if (placed) continue;

                float floor = shelves.Count == 0 ? padding : shelves[shelves.Count - 1].Ceil + padding;
                var newShelf = new Shelf(floor, island.size.y);
                if (!newShelf.TryPlace(island, padding)) return false; // 幅1を超えるアイランド
                shelves.Add(newShelf);
            }

            return shelves.Count == 0 || shelves[shelves.Count - 1].Ceil + padding <= 1f;
        }

        private class Shelf
        {
            public readonly float Floor;
            public readonly float Height;
            public float Ceil => Floor + Height;
            private readonly List<Island> lower = new List<Island>();
            private readonly List<Island> upper = new List<Island>();

            public Shelf(float floor, float height)
            {
                Floor = floor;
                Height = height;
            }

            public bool TryPlace(Island island, float padding)
            {
                if (island.size.y > Height + 1e-6f) return false;

                // 床側: 左から詰める。天井側の島と縦に干渉しない範囲まで。
                float xMin = lower.Count == 0 ? 0f : lower[lower.Count - 1].pos.x + lower[lower.Count - 1].size.x;
                float xMax = 1f;
                foreach (Island u in upper)
                {
                    if (island.size.y + u.size.y + padding * 2f > Height) xMax = Mathf.Min(xMax, u.pos.x);
                }
                if (xMax - xMin >= island.size.x + padding * 2f)
                {
                    island.pos = new Vector2(xMin + padding, Floor);
                    lower.Add(island);
                    return true;
                }

                // 天井側: 右から詰める。床側の島と縦に干渉しない範囲まで。
                float uMax = upper.Count == 0 ? 1f : upper[upper.Count - 1].pos.x;
                float uMin = 0f;
                foreach (Island l in lower)
                {
                    if (island.size.y + l.size.y + padding * 2f > Height) uMin = Mathf.Max(uMin, l.pos.x + l.size.x);
                }
                if (uMax - uMin >= island.size.x + padding * 2f)
                {
                    island.pos = new Vector2(uMax - island.size.x - padding, Ceil - island.size.y);
                    upper.Add(island);
                    return true;
                }

                return false;
            }
        }

        // ---------------------------------------------------------------
        // UV書き換えとアトラス描画
        // ---------------------------------------------------------------

        private static void RemapUVs(List<Island> islands)
        {
            foreach (Island island in islands)
            {
                Vector2 srcSize = island.SrcSize;
                foreach (int v in island.vertices)
                {
                    Vector2 uv = island.part.uv[v];
                    float fu = (uv.x - island.srcMin.x) / srcSize.x;
                    float fv = (uv.y - island.srcMin.y) / srcSize.y;
                    island.part.uv[v] = island.rotated
                        // 90度回転: 転置ピクセルコピーと整合する写像
                        ? island.pos + new Vector2((1f - fv) * island.size.x, fu * island.size.y)
                        : island.pos + new Vector2(fu * island.size.x, fv * island.size.y);
                }
            }
        }

        private static Texture2D RenderAtlas(
            List<Island> islands, string property, bool isMainProperty, int atlasSize, int paddingPx)
        {
            bool isNormal = MaterialAtlasBuilder.IsNormalProperty(property);
            bool linear = MaterialAtlasBuilder.IsLinearProperty(property);
            var atlas = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, false, linear: linear);

            Color32 fill = isNormal ? new Color32(128, 128, 255, 255) : new Color32(0, 0, 0, 255);
            var fillPixels = new Color32[atlasSize * atlasSize];
            for (int i = 0; i < fillPixels.Length; i++) fillPixels[i] = fill;
            atlas.SetPixels32(fillPixels);

            int bleed = Mathf.Max(1, paddingPx / 2);

            foreach (Island island in islands)
            {
                Material material = island.group.material;
                Texture texture = (material != null && material.HasProperty(property))
                    ? material.GetTexture(property)
                    : null;

                // アトラス上の描画先（にじみ対策で外周bleedピクセル分広げる）
                int dstX = Mathf.RoundToInt(island.pos.x * atlasSize) - bleed;
                int dstY = Mathf.RoundToInt(island.pos.y * atlasSize) - bleed;
                int dstW = Mathf.Max(1, Mathf.RoundToInt(island.size.x * atlasSize)) + bleed * 2;
                int dstH = Mathf.Max(1, Mathf.RoundToInt(island.size.y * atlasSize)) + bleed * 2;

                if (texture == null)
                {
                    if (!isMainProperty) continue; // 既定色のまま
                    Color color = material != null && material.HasProperty("_Color") ? material.color : Color.white;
                    FillRegion(atlas, dstX, dstY, dstW, dstH, color);
                    continue;
                }

                // ソース側の切り出し範囲（dst拡張と同じ比率で広げる）
                Vector2 srcSize = island.SrcSize;
                int innerW = dstW - bleed * 2;
                int innerH = dstH - bleed * 2;
                float bleedFracX = bleed / (float)innerW;
                float bleedFracY = bleed / (float)innerH;
                // 回転時はdstのX軸がソースのV軸に対応する
                float expandU = (island.rotated ? bleedFracY : bleedFracX) * srcSize.x;
                float expandV = (island.rotated ? bleedFracX : bleedFracY) * srcSize.y;
                var srcRect = new Rect(
                    island.srcMin.x - expandU, island.srcMin.y - expandV,
                    srcSize.x + expandU * 2f, srcSize.y + expandV * 2f);

                // 回転時は転置するため、コピーは幅と高さを入れ替えて取得する
                int copyW = island.rotated ? dstH : dstW;
                int copyH = island.rotated ? dstW : dstH;
                Texture2D copy = MaterialAtlasBuilder.CopyTextureRegion(texture, srcRect, copyW, copyH, linear, isNormal);
                try
                {
                    Color32[] pixels = copy.GetPixels32();
                    if (island.rotated) pixels = Transpose(pixels, copyW, copyH);
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
