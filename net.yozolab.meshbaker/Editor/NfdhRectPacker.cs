using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YozoLab.MeshBaker
{
    /// <summary>
    /// 矩形群を[0,1]²へ詰める汎用パッカー。
    /// UV0のアイランドアトラス（UVIslandAtlasPacker）とライトマップUV2の再パック
    /// （LightmapUVPacker）で共用する。
    ///
    /// アルゴリズムは TexTransCore (https://github.com/ReinaS-64892/TexTransCore) および
    /// TexTransTool (https://github.com/ReinaS-64892/TexTransTool) を参照して実装している
    /// (いずれも MIT License, Copyright (c) 2023 Reina_Sakiria):
    /// - パッキング: NFDH Plus FC — 縦長の矩形を90度回転して高さ降順に整列し、
    ///   棚(シェルフ)の床側は左から、天井側は右から詰めるFloor-Ceiling法 (NFDHPlasFC)
    /// - 充填率最適化: 全体を面積比0.5相当へ縮小してから、収まらなくなる直前まで
    ///   拡大を繰り返す探索 (IslandRelocationManager.RelocateLoop)
    /// </summary>
    internal static class NfdhRectPacker
    {
        /// <summary>
        /// パッキング対象の矩形。baseSizeに探索スケールを掛けたsizeで詰められ、
        /// 成功時はpos/size/rotatedに確定レイアウトが入る。
        /// </summary>
        internal class Item
        {
            /// <summary>スケール1のときの相対サイズ</summary>
            public Vector2 baseSize;

            // パッキング作業用と確定結果
            public Vector2 size;
            public Vector2 pos;
            public bool rotated;
            private Vector2 bestSize;
            private Vector2 bestPos;
            private bool bestRotated;

            internal void SaveBest()
            {
                bestSize = size;
                bestPos = pos;
                bestRotated = rotated;
            }

            internal void RestoreBest()
            {
                size = bestSize;
                pos = bestPos;
                rotated = bestRotated;
            }
        }

        /// <summary>
        /// 面積比0.5相当から開始し、収まらなくなる直前までスケールを拡大する。
        /// 成功時は各Itemに最良レイアウトを書き込み、採用したスケールを返す。失敗時は-1。
        /// </summary>
        internal static float PackWithScaleSearch(IReadOnlyList<Item> items, float padding)
        {
            float areaSum = items.Sum(i => i.baseSize.x * i.baseSize.y);

            for (float budget = 0.5f; budget > 0.02f; budget -= 0.05f)
            {
                float scale = Mathf.Sqrt(budget / Mathf.Max(areaSum, 1e-9f));
                if (!TryPackAtScale(items, padding, scale)) continue;

                foreach (Item item in items) item.SaveBest();
                float best = scale;
                for (int step = 0; step < 64; step++)
                {
                    float trial = best * 1.02f;
                    if (!TryPackAtScale(items, padding, trial)) break;
                    foreach (Item item in items) item.SaveBest();
                    best = trial;
                }
                foreach (Item item in items) item.RestoreBest();
                return best;
            }
            return -1f;
        }

        private static bool TryPackAtScale(IReadOnlyList<Item> items, float padding, float scale)
        {
            float maxLength = 1f - padding * 2f - 0.001f;
            foreach (Item item in items)
            {
                Vector2 size = item.baseSize * scale;
                // 1つでも枠を超える矩形があると全体が破綻するため、その矩形だけ縮める
                float longest = Mathf.Max(size.x, size.y);
                if (longest > maxLength) size *= maxLength / longest;
                item.size = size;
                item.rotated = false;
            }
            return TryPack(items, padding);
        }

        /// <summary>NFDH+FC: 高さ降順で、各棚の床(左から)と天井(右から)に詰める</summary>
        private static bool TryPack(IReadOnlyList<Item> items, float padding)
        {
            // 縦長の矩形は90度回転して横長に揃える
            foreach (Item item in items)
            {
                if (item.size.y > item.size.x)
                {
                    item.size = new Vector2(item.size.y, item.size.x);
                    item.rotated = true;
                }
            }

            List<Item> order = items.OrderByDescending(i => i.size.y).ToList();
            var shelves = new List<Shelf>();

            foreach (Item item in order)
            {
                bool placed = false;
                foreach (Shelf shelf in shelves)
                {
                    if (shelf.TryPlace(item, padding)) { placed = true; break; }
                }
                if (placed) continue;

                float floor = shelves.Count == 0 ? padding : shelves[shelves.Count - 1].Ceil + padding;
                var newShelf = new Shelf(floor, item.size.y);
                if (!newShelf.TryPlace(item, padding)) return false; // 幅1を超える矩形
                shelves.Add(newShelf);
            }

            return shelves.Count == 0 || shelves[shelves.Count - 1].Ceil + padding <= 1f;
        }

        private class Shelf
        {
            public readonly float Floor;
            public readonly float Height;
            public float Ceil => Floor + Height;
            private readonly List<Item> lower = new List<Item>();
            private readonly List<Item> upper = new List<Item>();

            public Shelf(float floor, float height)
            {
                Floor = floor;
                Height = height;
            }

            public bool TryPlace(Item item, float padding)
            {
                if (item.size.y > Height + 1e-6f) return false;

                // 床側: 左から詰める。天井側の矩形と縦に干渉しない範囲まで。
                float xMin = lower.Count == 0 ? 0f : lower[lower.Count - 1].pos.x + lower[lower.Count - 1].size.x;
                float xMax = 1f;
                foreach (Item u in upper)
                {
                    if (item.size.y + u.size.y + padding * 2f > Height) xMax = Mathf.Min(xMax, u.pos.x);
                }
                if (xMax - xMin >= item.size.x + padding * 2f)
                {
                    item.pos = new Vector2(xMin + padding, Floor);
                    lower.Add(item);
                    return true;
                }

                // 天井側: 右から詰める。床側の矩形と縦に干渉しない範囲まで。
                float uMax = upper.Count == 0 ? 1f : upper[upper.Count - 1].pos.x;
                float uMin = 0f;
                foreach (Item l in lower)
                {
                    if (item.size.y + l.size.y + padding * 2f > Height) uMin = Mathf.Max(uMin, l.pos.x + l.size.x);
                }
                if (uMax - uMin >= item.size.x + padding * 2f)
                {
                    item.pos = new Vector2(uMax - item.size.x - padding, Ceil - item.size.y);
                    upper.Add(item);
                    return true;
                }

                return false;
            }
        }
    }
}
