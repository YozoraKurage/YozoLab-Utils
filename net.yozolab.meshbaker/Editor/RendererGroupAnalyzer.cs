using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YozoLab.MeshBaker
{
    /// <summary>
    /// Renderer群を解析して出力グループ分けの提案を作る（ドラッグ＆ドロップの自動解析用）。
    ///
    /// 方針:
    /// - まずマテリアルの品質クラスで分離する:
    ///   - Transparent（RenderQueue &gt; 2500）: 統合マテリアルは1つしか作られないため、
    ///     不透明と混ぜると描画設定が壊れる。独立グループに分離して扱いを判断できるようにする。
    ///   - ColorOnly（全マテリアルがテクスチャ未割り当て）: ユニークなテクセル領域を必要としない
    ///     低コストなオブジェクト群。マテリアルが違っても1つのまとまりに集約し、
    ///     高品質（テクスチャ持ち）のオブジェクト群とグループを分ける。
    /// - テクスチャ持ちは、マテリアル統合が無効な場合のみマテリアル構成が同じRenderer同士でまとめる
    ///   （非統合出力ではサブメッシュ数＝マテリアル数になるため、混在を避けると出力が整理される）。
    ///   統合が有効な場合はアトラス/マテリアルが全グループ共有なので、この分割は行わない。
    /// - 各まとまりの中では、位置の近いRenderer同士から貪欲にクラスタリングし、
    ///   1グループの頂点数がバジェット（UInt16インデックスの上限目安）を超えないよう分割する。
    ///   空間的にまとまったグループはフラスタムカリングにも有利。
    /// </summary>
    internal static class RendererGroupAnalyzer
    {
        /// <summary>1グループの頂点数バジェット（UInt16インデックスで収まる目安）</summary>
        internal const int DefaultVertexBudget = 65000;

        /// <summary>これより大きいRenderQueueを透明として扱う（Transparent=3000, AlphaTest=2450）</summary>
        private const int TransparentQueueThreshold = 2500;

        internal class Proposal
        {
            public string name;
            public readonly List<Renderer> renderers = new List<Renderer>();
            public int vertexCount;
        }

        private class Entry
        {
            public Renderer renderer;
            public Vector3 center;
            public int vertexCount;
        }

        internal static List<Proposal> Analyze(
            List<Renderer> renderers, bool splitByMaterialSet, int vertexBudget)
        {
            // バケット分け（品質クラス → マテリアル構成の順で判定）
            var buckets = new List<(string baseName, List<Entry> entries)>();
            var byKey = new Dictionary<string, int>();
            foreach (Renderer renderer in renderers)
            {
                Mesh mesh = StaticMeshBaker.GetSharedMesh(renderer);
                if (mesh == null) continue;
                var entry = new Entry
                {
                    renderer = renderer,
                    center = renderer.bounds.center,
                    vertexCount = mesh.vertexCount,
                };

                string key;
                string baseName;
                if (IsTransparent(renderer))
                {
                    key = "::transparent";
                    baseName = "Transparent";
                }
                else if (IsColorOnly(renderer))
                {
                    // 色のみのオブジェクトは、マテリアルが違っても1つのまとまりに集約する
                    key = "::coloronly";
                    baseName = "ColorOnly";
                }
                else if (splitByMaterialSet)
                {
                    key = MaterialSetKey(renderer);
                    baseName = BucketBaseName(renderer, true);
                }
                else
                {
                    key = "";
                    baseName = "Area";
                }

                if (!byKey.TryGetValue(key, out int bucketIndex))
                {
                    bucketIndex = buckets.Count;
                    byKey.Add(key, bucketIndex);
                    buckets.Add((baseName, new List<Entry>()));
                }
                buckets[bucketIndex].entries.Add(entry);
            }

            // バケットごとに空間クラスタリングして提案を作る
            var proposals = new List<Proposal>();
            var usedNames = new HashSet<string>();
            foreach ((string baseName, List<Entry> entries) in buckets)
            {
                List<Proposal> clusters = BuildClusters(entries, vertexBudget);
                for (int i = 0; i < clusters.Count; i++)
                {
                    string name = clusters.Count == 1 ? baseName : $"{baseName}{i + 1}";
                    int serial = 1;
                    while (!usedNames.Add(name)) name = $"{baseName}_{++serial}";
                    clusters[i].name = name;
                    proposals.Add(clusters[i]);
                }
            }
            return proposals;
        }

        /// <summary>
        /// 位置の近いRenderer同士を、頂点数バジェットを超えない範囲で貪欲にまとめる。
        /// シードから始めて、クラスタ重心に最も近いRendererを追加し続ける。
        /// </summary>
        private static List<Proposal> BuildClusters(List<Entry> entries, int vertexBudget)
        {
            var clusters = new List<Proposal>();
            // 座標順に並べてシード選択を決定的にする
            var remaining = entries
                .OrderBy(e => e.center.x).ThenBy(e => e.center.z).ThenBy(e => e.center.y)
                .ToList();

            while (remaining.Count > 0)
            {
                Entry seed = remaining[0];
                remaining.RemoveAt(0);
                var cluster = new Proposal { vertexCount = seed.vertexCount };
                cluster.renderers.Add(seed.renderer);
                Vector3 centroid = seed.center;

                while (remaining.Count > 0)
                {
                    int bestIndex = -1;
                    float bestDistance = float.MaxValue;
                    for (int i = 0; i < remaining.Count; i++)
                    {
                        if (cluster.vertexCount + remaining[i].vertexCount > vertexBudget) continue;
                        float distance = (remaining[i].center - centroid).sqrMagnitude;
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            bestIndex = i;
                        }
                    }
                    if (bestIndex < 0) break;

                    Entry picked = remaining[bestIndex];
                    remaining.RemoveAt(bestIndex);
                    cluster.renderers.Add(picked.renderer);
                    cluster.vertexCount += picked.vertexCount;
                    centroid += (picked.center - centroid) / cluster.renderers.Count;
                }
                clusters.Add(cluster);
            }
            return clusters;
        }

        /// <summary>いずれかのマテリアルが透明系（RenderQueueがしきい値超）か</summary>
        private static bool IsTransparent(Renderer renderer)
        {
            return renderer.sharedMaterials.Any(
                m => m != null && m.renderQueue > TransparentQueueThreshold);
        }

        /// <summary>全マテリアルがテクスチャ未割り当て（単色のみ）か</summary>
        private static bool IsColorOnly(Renderer renderer)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                foreach (string property in material.GetTexturePropertyNames())
                {
                    if (material.GetTexture(property) != null) return false;
                }
            }
            return true;
        }

        /// <summary>マテリアル構成のキー（順不同・null除外のインスタンスID列）</summary>
        private static string MaterialSetKey(Renderer renderer)
        {
            return string.Join(",", renderer.sharedMaterials
                .Where(m => m != null)
                .Select(m => m.GetInstanceID())
                .OrderBy(id => id));
        }

        private static string BucketBaseName(Renderer renderer, bool splitByMaterialSet)
        {
            if (!splitByMaterialSet) return "Area";
            Material first = renderer.sharedMaterials.FirstOrDefault(m => m != null);
            return first != null ? SanitizeName(first.name) : "NoMaterial";
        }

        /// <summary>アセットパスに使える文字だけに整形する（グループ名は出力ファイル名になるため）</summary>
        private static string SanitizeName(string name)
        {
            var builder = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
            {
                builder.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            }
            return builder.Length > 0 ? builder.ToString() : "Group";
        }
    }
}
