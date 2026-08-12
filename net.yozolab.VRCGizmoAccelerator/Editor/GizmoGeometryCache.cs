using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// コンポーネントごとに、前回描いたギズモの頂点をそのまま取っておく。
    ///
    /// VRCPhysBoneEditor.Draw はカメラに一切依存していない
    /// （HandleUtility / Camera / SceneView を呼んでいない）。つまり階層と設定が
    /// 同じなら、毎フレーム同じ頂点を作り直しているだけになる。
    /// ここに取っておけば、変化が無い間は Draw() ごと飛ばして、
    /// 出来上がったメッシュを描くだけで済む。
    ///
    /// ただしコライダーの描画には 1 箇所だけ HandleUtility.GetHandleSize がある。
    /// カメラ依存のものを固めてしまうと、カメラを動かしたときに古い大きさのまま
    /// 残る。そこで「捕捉中に GetHandleSize が呼ばれたか」を見て、呼ばれていた
    /// コンポーネントはキャッシュしない（元の毎フレーム描画に落ちる）。
    /// </summary>
    internal static class GizmoGeometryCache
    {
        internal sealed class Entry
        {
            internal readonly MeshBuffer Lines = new MeshBuffer(MeshTopology.Lines);
            internal readonly MeshBuffer Triangles = new MeshBuffer(MeshTopology.Triangles);

            internal int version = -1;
            internal int fingerprint;
            internal int cameraFingerprint;
            internal double builtAt;
            internal bool cameraDependent;

            internal int VertexCount => Lines.VertexCount + Triangles.VertexCount;

            internal void Release()
            {
                Lines.Release();
                Triangles.Release();
            }
        }

        private static readonly Dictionary<int, Entry> Entries = new Dictionary<int, Entry>();

        /// <summary>
        /// 取っておく頂点数の上限。1 コンポーネントあたり数万頂点になるので、
        /// 際限なく抱えるとメモリを食う。超えたら古いものから捨てる。
        /// </summary>
        internal const int MaxTotalVertices = 2_000_000;

        /// <summary>
        /// 保険。姿勢の指紋を毎フレーム見ているので、これは
        /// 「指紋にも変更イベントにも出てこない何か」に対する最後の砦でしかない。
        /// </summary>
        internal const double MaxStaleSeconds = 2.0;

        private static int _totalVertices;

        internal static int Hits { get; private set; }
        internal static int Misses { get; private set; }
        internal static int CachedComponents => Entries.Count;
        internal static int CachedVertices => _totalVertices;

        internal static void ResetStats()
        {
            Hits = 0;
            Misses = 0;
        }

        internal static void Clear()
        {
            foreach (var entry in Entries.Values)
            {
                _totalVertices -= entry.VertexCount;
                entry.Release();
            }
            Entries.Clear();
            _totalVertices = 0;
            CombinedGizmoMesh.Clear();
        }

        /// <summary>
        /// 使える状態のキャッシュがあれば返す。
        ///
        /// カメラ依存の値（GetHandleSize）を使って作られたものは、カメラが
        /// 動いていない間だけ使える。以前は諦めて毎フレーム作り直していたが、
        /// コライダーのギズモがそれで 9ms 使っていた。
        /// </summary>
        internal static Entry Find(int id, int fingerprint, int cameraFingerprint)
        {
            if (!Entries.TryGetValue(id, out var entry)) return null;
            if (entry.version != InvalidationVersion.Current) return null;
            if (entry.cameraDependent && entry.cameraFingerprint != cameraFingerprint) return null;
            if (entry.fingerprint != fingerprint) return null;   // ボーンが動いた
            if (EditorApplication.timeSinceStartup - entry.builtAt > MaxStaleSeconds) return null;
            return entry;
        }

        /// <summary>状態を問わず、あるものをそのまま返す（束ね直し用）。</summary>
        internal static Entry Peek(int id)
        {
            Entries.TryGetValue(id, out var entry);
            return entry;
        }

        /// <summary>これから作り直すぶんの入れ物を用意する。</summary>
        internal static Entry Prepare(int id)
        {
            if (Entries.TryGetValue(id, out var entry))
            {
                _totalVertices -= entry.VertexCount;
            }
            else
            {
                entry = new Entry();
                Entries[id] = entry;
            }

            entry.version = -1;
            entry.cameraDependent = false;
            entry.Lines.Clear();
            entry.Triangles.Clear();
            return entry;
        }

        /// <summary>作り終えたものを有効にする。</summary>
        internal static void Commit(Entry entry, bool cameraDependent, int fingerprint, int cameraFingerprint)
        {
            entry.cameraDependent = cameraDependent;
            entry.fingerprint = fingerprint;
            entry.cameraFingerprint = cameraFingerprint;
            entry.version = InvalidationVersion.Current;
            entry.builtAt = EditorApplication.timeSinceStartup;

            _totalVertices += entry.VertexCount;
            Misses++;
            CombinedGizmoMesh.Invalidate();

            Evict();
        }

        internal static void CountHit() => Hits++;

        private static void Evict()
        {
            if (_totalVertices <= MaxTotalVertices) return;

            // 古いものから捨てる。件数は多くないので素直に走査する。
            var ordered = new List<KeyValuePair<int, Entry>>(Entries);
            ordered.Sort((a, b) => a.Value.builtAt.CompareTo(b.Value.builtAt));

            foreach (var pair in ordered)
            {
                if (_totalVertices <= MaxTotalVertices) break;

                _totalVertices -= pair.Value.VertexCount;
                pair.Value.Release();
                Entries.Remove(pair.Key);
            }
        }
    }
}
