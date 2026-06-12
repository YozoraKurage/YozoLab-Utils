using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YozoLab.MeshBaker
{
    /// <summary>
    /// Renderer群を解析して出力グループ分けの提案を作る（ドラッグ＆ドロップの自動解析用）。
    ///
    /// 方針:
    /// - マテリアル統合が無効な場合は、まずマテリアル構成が同じRenderer同士でまとめる
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
            // バケット分け（マテリアル構成 or 全体で1つ）
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

                string key = splitByMaterialSet ? MaterialSetKey(renderer) : "";
                if (!byKey.TryGetValue(key, out int bucketIndex))
                {
                    bucketIndex = buckets.Count;
                    byKey.Add(key, bucketIndex);
                    buckets.Add((BucketBaseName(renderer, splitByMaterialSet), new List<Entry>()));
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
