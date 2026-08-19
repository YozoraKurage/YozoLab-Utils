using UnityEngine;
using UnityEngine.Rendering;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// 頂点を溜めて、1 つのメッシュとして一括で描く。
    ///
    /// GL.Vertex / GL.Color は 1 頂点あたり 1 回のネイティブ呼び出しになるので、
    /// 頂点数が数十万になると managed 側だけで数 ms かかる。メッシュにまとめれば
    /// 転送は数回の呼び出しで済む。
    /// </summary>
    internal sealed class MeshBuffer
    {
        // 中身は毎回こちらで正しく作るので、Unity 側の検証と通知は要らない。
        // インデックスの検証は頂点数に比例するので、これが地味に効く。
        private const MeshUpdateFlags UpdateFlags = MeshUpdateFlags.DontValidateIndices
                                                    | MeshUpdateFlags.DontRecalculateBounds
                                                    | MeshUpdateFlags.DontNotifyMeshUsers;

        private readonly MeshTopology _topology;

        // List<T>.Add は 1 頂点あたりの呼び出しコストが効いてくる（236,000 頂点で
        // 実測 6ms）。配列に直接書き込み、必要になったときだけ倍に伸ばす。
        private Vector3[] _vertices = new Vector3[8192];
        private Color[] _colors = new Color[8192];
        private int _count;

        private int[] _indices = new int[0];
        private Vector3[] _normals = new Vector3[0];
        private Mesh _mesh;

        internal int VertexCount => _count;

        // 実験モード（Gizmos 経由の描画）から中身を読むため
        internal Vector3[] RawVertices => _vertices;
        internal Color[] RawColors => _colors;

        /// <summary>描かずにメッシュだけ用意する（Gizmos.DrawMesh に渡す用）。</summary>
        internal Mesh GetMesh(bool withNormals = false)
        {
            if (_count == 0) return null;

            EnsureMesh();
            EnsureIndices(_count);

            _mesh.Clear(true);
            _mesh.SetVertices(_vertices, 0, _count);
            _mesh.SetColors(_colors, 0, _count);
            _mesh.SetIndices(_indices, 0, _count, _topology, 0, false);

            if (withNormals && _topology == MeshTopology.Triangles)
            {
                // Gizmos.DrawMesh は法線を要求する。向きは見た目に影響しないので
                // 使い回しの配列を一定値で埋めるだけでよい。
                EnsureNormals(_count);
                _mesh.SetNormals(_normals, 0, _count, UpdateFlags);
            }

            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);
            return _mesh;
        }

        internal MeshBuffer(MeshTopology topology)
        {
            _topology = topology;
        }

        internal void Add(Vector3 vertex, Color color)
        {
            if (_count == _vertices.Length) EnsureCapacity(_count + 1);

            _vertices[_count] = vertex;
            _colors[_count] = color;
            _count++;
        }

        /// <summary>
        /// 少なくとも needed 個入るまで伸ばす。
        ///
        /// Release() 後は配列長が 0 になっているので、「倍にする」だけだと
        /// 0 のまま伸びず無限ループになる。下限を必ず設けること。
        /// </summary>
        private void EnsureCapacity(int needed)
        {
            if (_vertices.Length >= needed) return;

            int size = Mathf.Max(8192, _vertices.Length);
            while (size < needed) size *= 2;

            var vertices = new Vector3[size];
            var colors = new Color[size];
            System.Array.Copy(_vertices, vertices, _count);
            System.Array.Copy(_colors, colors, _count);
            _vertices = vertices;
            _colors = colors;
        }

        internal void Clear()
        {
            _count = 0;
        }

        /// <summary>別のバッファの中身を末尾に足す（束ね直し用）。</summary>
        internal void Append(MeshBuffer other)
        {
            if (other._count == 0) return;

            EnsureCapacity(_count + other._count);

            System.Array.Copy(other._vertices, 0, _vertices, _count, other._count);
            System.Array.Copy(other._colors, 0, _colors, _count, other._count);
            _count += other._count;
        }

        /// <summary>抱えているメッシュと配列を手放す。</summary>
        internal void Release()
        {
            _count = 0;
            _vertices = new Vector3[0];
            _colors = new Color[0];
            _indices = new int[0];
            _normals = new Vector3[0];

            if (_mesh != null)
            {
                Object.DestroyImmediate(_mesh);
                _mesh = null;
            }
        }

        internal void Draw()
        {
            if (_count == 0) return;

            EnsureMesh();
            EnsureIndices(_count);

            // 中身は毎回こちらで正しく作るので、Unity 側の検証と通知は要らない。
            // インデックスの検証は頂点数に比例するので、これが地味に効く。
            _mesh.Clear(true);
            _mesh.SetVertices(_vertices, 0, _count, UpdateFlags);
            _mesh.SetColors(_colors, 0, _count, UpdateFlags);
            // 頂点は既に並び順どおりなので、インデックスは 0..n-1 の連番でよい。
            // 境界は毎回計算させず十分大きい固定値にする。
            _mesh.SetIndices(_indices, 0, _count, _topology, 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);

            Graphics.DrawMeshNow(_mesh, Matrix4x4.identity);
        }

        private void EnsureMesh()
        {
            if (_mesh != null) return;

            _mesh = new Mesh
            {
                name = "VRCGizmoAccelerator",
                // 6 万頂点を超えるので 32bit インデックスが要る
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _mesh.MarkDynamic();
        }

        private void EnsureNormals(int count)
        {
            if (_normals.Length >= count) return;

            int size = Mathf.Max(count, _normals.Length * 2, 1024);
            _normals = new Vector3[size];
            for (int i = 0; i < size; i++) _normals[i] = Vector3.up;
        }

        private void EnsureIndices(int count)
        {
            if (_indices.Length >= count) return;

            int size = Mathf.Max(count, _indices.Length * 2, 8192);
            var indices = new int[size];
            for (int i = 0; i < size; i++) indices[i] = i;
            _indices = indices;
        }
    }
}
