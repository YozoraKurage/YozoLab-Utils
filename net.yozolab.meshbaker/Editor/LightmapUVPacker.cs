using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YozoLab.MeshBaker
{
    /// <summary>
    /// ライトマップ用UV2の保持と再パック。
    ///
    /// 各パーツが元メッシュに持っていたUV2（手動展開・モデルインポータの自動展開どちらも）を
    /// そのまま尊重し、UV2を持たないパーツだけをパーツ単体で自動展開する。
    /// その上で、全パーツのUV2チャートをワールド表面積に比例したテクセル密度で
    /// [0,1]に再パックし、結合メッシュ全体で重なりのないライトマップUVを作る。
    /// </summary>
    internal static class LightmapUVPacker
    {
        /// <summary>チャート間の余白（UV空間）。1024pxのライトマップで約4テクセル相当。</summary>
        private const float Padding = 4f / 1024f;

        /// <summary>パーツのUV2チャート一式を1つの矩形として詰める</summary>
        private class ChartItem : NfdhRectPacker.Item
        {
            public BakePart part;
            public Vector2 srcMin;
            public Vector2 srcSize;
        }

        /// <summary>
        /// 結合対象パーツのUV2を整える。
        /// 既存UV2を持つパーツはそれを保持し、持たないパーツは単体で自動展開した上で、
        /// 全パーツ分を1枚のライトマップレイアウトへ再パックする。
        /// </summary>
        internal static void PreserveAndRepack(List<BakePart> parts, List<string> warnings)
        {
            if (parts.Count == 0) return;

            foreach (BakePart part in parts)
            {
                // UV2が無い、または中身が退化している（全ゼロ等でチャート面積を持たない）パーツは自動展開する
                if (part.uv2 == null || part.uv2.Length != part.positions.Length || UVArea(part) <= 1e-12f)
                {
                    GeneratePartUV2(part);
                }
            }

            var items = new List<ChartItem>();
            foreach (BakePart part in parts)
            {
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                foreach (Vector2 uv in part.uv2)
                {
                    min = Vector2.Min(min, uv);
                    max = Vector2.Max(max, uv);
                }
                Vector2 size = Vector2.Max(max - min, new Vector2(1e-6f, 1e-6f));

                // テクセル密度をワールド表面積に揃える:
                // チャートをf倍すると密度はf²·uvArea/worldAreaになるため、
                // f ∝ sqrt(worldArea/uvArea) で全パーツの密度が均一になる
                float worldArea = SurfaceArea(part);
                float uvArea = UVArea(part);
                float densityScale = (worldArea > 1e-12f && uvArea > 1e-12f)
                    ? Mathf.Sqrt(worldArea / uvArea)
                    : 1f;

                items.Add(new ChartItem
                {
                    part = part,
                    srcMin = min,
                    srcSize = size,
                    baseSize = size * densityScale,
                });
            }

            float scale = NfdhRectPacker.PackWithScaleSearch(items, Padding);
            if (scale <= 0f)
            {
                warnings.Add("ライトマップUV(UV2)の再パックに失敗したため、元のUV2のまま出力しました。" +
                             "パーツ間でUV2が重なる場合があります。");
                return;
            }

            foreach (ChartItem item in items)
            {
                Vector2[] uv2 = item.part.uv2;
                for (int i = 0; i < uv2.Length; i++)
                {
                    float fu = (uv2[i].x - item.srcMin.x) / item.srcSize.x;
                    float fv = (uv2[i].y - item.srcMin.y) / item.srcSize.y;
                    uv2[i] = item.rotated
                        ? item.pos + new Vector2((1f - fv) * item.size.x, fu * item.size.y)
                        : item.pos + new Vector2(fu * item.size.x, fv * item.size.y);
                }
            }
        }

        /// <summary>
        /// UV2を持たないパーツを単体メッシュとして自動展開する。
        /// 展開時にチャートのシームで頂点分割が起きることがあるため、
        /// 全頂点属性をメッシュから読み戻してパーツを再構成する。
        /// </summary>
        private static void GeneratePartUV2(BakePart part)
        {
            // 展開で頂点が増えて65535を超える可能性があるため、一時メッシュは常に32bitインデックスにする
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            try
            {
                mesh.vertices = part.positions;
                mesh.normals = part.normals;
                mesh.tangents = part.tangents;
                mesh.uv = part.uv;
                for (int ch = 0; ch < BakePart.ExtraUVChannels; ch++)
                {
                    if (part.extraUVs[ch] != null) mesh.SetUVs(ch + 2, part.extraUVs[ch]);
                }
                if (part.colors != null) mesh.colors32 = part.colors;
                mesh.triangles = part.indices;

                Unwrapping.GenerateSecondaryUVSet(mesh);

                part.positions = mesh.vertices;
                part.normals = mesh.normals;
                part.tangents = mesh.tangents;
                part.uv = mesh.uv;
                var channelBuffer = new List<Vector2>();
                for (int ch = 0; ch < BakePart.ExtraUVChannels; ch++)
                {
                    if (part.extraUVs[ch] == null) continue;
                    mesh.GetUVs(ch + 2, channelBuffer);
                    part.extraUVs[ch] = channelBuffer.ToArray();
                }
                if (part.colors != null) part.colors = mesh.colors32;
                part.uv2 = mesh.uv2;
                part.indices = mesh.triangles;
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>ベイク済み座標（アセンブリローカル空間）での総表面積</summary>
        private static float SurfaceArea(BakePart part)
        {
            float area = 0f;
            for (int i = 0; i < part.indices.Length; i += 3)
            {
                Vector3 p0 = part.positions[part.indices[i]];
                Vector3 p1 = part.positions[part.indices[i + 1]];
                Vector3 p2 = part.positions[part.indices[i + 2]];
                area += Vector3.Cross(p1 - p0, p2 - p0).magnitude * 0.5f;
            }
            return area;
        }

        /// <summary>UV2空間での総三角形面積</summary>
        private static float UVArea(BakePart part)
        {
            float area = 0f;
            for (int i = 0; i < part.indices.Length; i += 3)
            {
                Vector2 u0 = part.uv2[part.indices[i]];
                Vector2 u1 = part.uv2[part.indices[i + 1]];
                Vector2 u2 = part.uv2[part.indices[i + 2]];
                Vector2 e1 = u1 - u0;
                Vector2 e2 = u2 - u0;
                area += Mathf.Abs(e1.x * e2.y - e1.y * e2.x) * 0.5f;
            }
            return area;
        }
    }
}
