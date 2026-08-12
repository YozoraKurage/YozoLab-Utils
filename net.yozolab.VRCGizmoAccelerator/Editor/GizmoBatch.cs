using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// 横取りした描画を、SetPass と GL.Begin をまとめた形で流す。
    ///
    /// 元の実装は図形 1 つごとに
    ///     ApplyWireMaterial() → GL.PushMatrix → GL.Begin → 頂点 → GL.End → GL.PopMatrix
    /// を発行する。ApplyWireMaterial は SetPass なので、これが図形数ぶん出るのが本題。
    ///
    /// ここでは区間ぶんの頂点を溜めて 1 つのメッシュにまとめ、
    /// マテリアルを 1 回当てて Graphics.DrawMeshNow で送る。
    ///
    /// 頂点ごとに GL.Vertex / GL.Color を呼ぶ方式（元の実装と同じやり方）も試したが、
    /// 1 頂点 1 回のネイティブ呼び出しになるため、236,000 頂点で 6ms かかる。
    /// メッシュにまとめれば転送は数回の呼び出しで済む。
    /// </summary>
    internal static class GizmoBatch
    {
        /// <summary>横取りした呼び出しをどう扱うか。</summary>
        internal enum Interception
        {
            /// <summary>区間の外。元の実装をそのまま走らせる。</summary>
            PassThrough,
            /// <summary>横取りする。</summary>
            Capture,
            /// <summary>捨てる。描画イベントではないので、描いても何も出ない。</summary>
            Skip,
        }

        // ギズモのコールバックは入れ子になる（PhysBone のギズモがコライダーの
        // ギズモを呼ぶ）。一番外側で閉じる。
        private static int _depth;

        // 描画イベントの最中か（OnSceneGUI は Layout などでも呼ばれる）
        private static bool _drawing;

        // 頂点は溜めてから一括で送る。GL.Vertex は 1 頂点 1 回のネイティブ呼び出しで、
        // 236,000 頂点だと実測 6ms かかる。メッシュにまとめれば数回の呼び出しで済む。
        private static readonly MeshBuffer SharedLines = new MeshBuffer(MeshTopology.Lines);
        private static readonly MeshBuffer SharedTriangles = new MeshBuffer(MeshTopology.Triangles);

        // キャッシュに残す場合は、共有バッファではなくそのコンポーネントの入れ物へ書く
        private static MeshBuffer Lines = SharedLines;
        private static MeshBuffer Triangles = SharedTriangles;
        private static GizmoGeometryCache.Entry _target;
        private static int _targetFingerprint;
        private static int _targetCameraFingerprint;

        // 束ねて描く設定のときは、ここでは描かずキャッシュに置くだけにする
        private static bool _suppressDraw;

        /// <summary>
        /// 捕捉中にカメラ依存のもの（HandleUtility.GetHandleSize）が参照されたか。
        /// 参照されていたらキャッシュしてはいけない。
        /// </summary>
        internal static bool CameraDependentSeen { get; private set; }

        internal static void MarkCameraDependent()
        {
            if (_depth > 0) CameraDependentSeen = true;
        }

        internal static Interception Mode
        {
            get
            {
                if (_depth <= 0) return Interception.PassThrough;
                if (_drawing) return Interception.Capture;

                // Layout や MouseMove で呼ばれた描画。元の実装はここでも
                // 全部の線を GL へ流すが、描画イベントではないので何も出ない。
                return VRCGizmoAcceleratorSettings.instance.skipNonDrawingEvents
                    ? Interception.Skip
                    : Interception.PassThrough;
            }
        }

        /// <summary>横取りを有効にしてよい区間にいるか。</summary>
        internal static bool IsActive => _depth > 0 && _drawing;

        /// <summary>
        /// 今このツール自身が発行している最中か。
        /// 区間を閉じたあとに流すので、IsActive では捉えられない。
        /// </summary>
        internal static bool Flushing { get; private set; }

        // 直近 1 区間ぶんの統計（ウィンドウ表示用）
        internal static int LastVertexCount { get; private set; }
        internal static int LastDrawCallCount { get; private set; }
        internal static int LastPrimitiveCount { get; private set; }
        internal static int LastSetPassCount { get; private set; }
        private static int _vertexCount;
        private static int _primitiveCount;
        private static int _drawCallCount;
        private static int _setPassCount;

        /// <summary>
        /// 横取りした図形をテストから覗くための差し込み口。
        /// 設定されている間は GL を触らずこちらへ渡す（GL は描画中しか使えないため）。
        /// </summary>
        internal interface ISink
        {
            void Line(Vector3 a, Vector3 b, Color color);
            void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color);
        }

        internal static ISink Sink;

        /// <summary>
        /// 横取りしてよいイベントか。
        ///
        /// ギズモのコールバックは GUI イベントの外から呼ばれるので Event.current は null。
        /// OnSceneGUI は Layout（当たり判定の登録）や MouseMove でも呼ばれるため、
        /// 描画イベントのときだけ横取りする。
        /// </summary>
        /// <summary>今このタイミングが描画イベントか。</summary>
        internal static bool IsDrawingEventNow => IsDrawingEvent(Event.current);

        internal static bool IsDrawingEvent(Event current)
        {
            return current == null || current.type == EventType.Repaint;
        }

        /// <summary>
        /// 今の区間がギズモのコールバックの中か。
        /// Gizmos.* はギズモのコールバックでしか使えないので、
        /// OnSceneGUI 由来の区間では即時描画に落とす必要がある。
        /// </summary>
        internal static bool InGizmoCallback { get; set; }

        internal static void Begin() => Begin(null, 0, 0, false);

        /// <param name="target">
        /// 結果を残しておく入れ物。null なら使い捨て（共有バッファへ書く）。
        /// </param>
        internal static void Begin(GizmoGeometryCache.Entry target, int fingerprint, int cameraFingerprint, bool suppressDraw)
        {
            _depth++;
            if (_depth > 1) return;

            _target = target;
            _targetFingerprint = fingerprint;
            _targetCameraFingerprint = cameraFingerprint;
            _suppressDraw = suppressDraw;
            Lines = target?.Lines ?? SharedLines;
            Triangles = target?.Triangles ?? SharedTriangles;
            CameraDependentSeen = false;

            _drawing = IsDrawingEvent(Event.current);
            _vertexCount = 0;
            _primitiveCount = 0;
            _drawCallCount = 0;
            _setPassCount = 0;
        }

        /// <summary>
        /// 入れ子の一番外側で閉じる。例外が飛んでも必ず呼ばれるよう、
        /// パッチ側では finalizer から呼ぶこと。
        /// </summary>
        internal static void End()
        {
            if (_depth <= 0)
            {
                Reset();
                return;
            }

            _depth--;
            if (_depth > 0) return;

            if (!_suppressDraw) DrawBuffers(Triangles, Lines);

            if (_target != null)
            {
                // 次のフレームで使い回すので消さない
                GizmoGeometryCache.Commit(_target, CameraDependentSeen, _targetFingerprint, _targetCameraFingerprint);
                _target = null;
            }
            else
            {
                Triangles.Clear();
                Lines.Clear();
            }

            Lines = SharedLines;
            Triangles = SharedTriangles;
            _suppressDraw = false;
            _drawing = false;

            LastVertexCount = _vertexCount;
            LastPrimitiveCount = _primitiveCount;
            LastDrawCallCount = _drawCallCount;
            LastSetPassCount = _setPassCount;
        }

        // ---- 送出 -------------------------------------------------------------

        private static void Draw(MeshBuffer buffer)
        {
            if (buffer.VertexCount == 0) return;

            Flushing = true;
            try
            {
                if (_setPassCount == 0)
                {
                    // 区間に 1 回だけ。元の実装はここを図形ごとに呼んでいた（= SetPass）。
                    HandlesMaterial.Apply();
                    _setPassCount++;
                }

                buffer.Draw();
                _drawCallCount++;
            }
            finally
            {
                Flushing = false;
            }
        }

        /// <summary>ドメインリロードやパッチ解除で状態を捨てる。</summary>
        internal static void Reset()
        {
            _depth = 0;
            _drawing = false;
            _target = null;
            SharedLines.Clear();
            SharedTriangles.Clear();
            Lines = SharedLines;
            Triangles = SharedTriangles;
        }

        /// <summary>キャッシュしてあるものをそのまま描く。SDK の Draw() は走らせない。</summary>
        internal static void DrawCached(GizmoGeometryCache.Entry entry)
        {
            DrawBuffers(entry.Triangles, entry.Lines);
        }

        /// <summary>用意済みのバッファを描く（SetPass はまとめて 1 回）。</summary>
        internal static void DrawBuffers(MeshBuffer triangles, MeshBuffer lines)
        {
            _drawCallCount = 0;
            _setPassCount = 0;

            var mode = VRCGizmoAcceleratorSettings.instance.drawMode;

            // Gizmos.* はギズモのコールバックの中でしか使えない。
            // コマンドバッファはどこからでも積める。
            if (mode == GizmoDrawMode.GizmoLines && InGizmoCallback && GizmoSubmitter.Available)
            {
                GizmoSubmitter.Submit(triangles, lines);
                LastVertexCount = triangles.VertexCount + lines.VertexCount;
                LastDrawCallCount = GizmoSubmitter.LastDrawCallCount;
                LastSetPassCount = 0;
                return;
            }

            // 塗りを先、線を後。塗りは線の下敷きなので、この順のほうが元の見え方に近い。
            Draw(triangles);
            Draw(lines);

            LastVertexCount = triangles.VertexCount + lines.VertexCount;
            LastDrawCallCount = _drawCallCount;
            LastSetPassCount = _setPassCount;
        }

        // ---- 頂点の投入 -------------------------------------------------------

        /// <summary>線分 1 本。座標はワールド空間で渡すこと。</summary>
        internal static void AddLine(Vector3 a, Vector3 b, Color color)
        {
            _primitiveCount++;
            _vertexCount += 2;

            if (Sink != null) { Sink.Line(a, b, color); return; }

            Lines.Add(a, color);
            Lines.Add(b, color);
        }

        /// <summary>
        /// 連続線（GL.LINE_STRIP 相当）を GL.LINES の頂点対として流す。
        /// 描かれる線分は LINE_STRIP と同一。
        /// </summary>
        internal static void AddLineStrip(Vector3[] points, int offset, int count, Color color, Matrix4x4 matrix)
        {
            if (count < 2) return;

            _primitiveCount += count - 1;
            _vertexCount += (count - 1) * 2;

            // Handles.matrix が単位行列のことは多い。その場合は変換を丸ごと飛ばす。
            bool identity = matrix.isIdentity;

            Vector3 prev = identity ? points[offset] : matrix.MultiplyPoint3x4(points[offset]);
            for (int i = 1; i < count; i++)
            {
                Vector3 cur = identity ? points[offset + i] : matrix.MultiplyPoint3x4(points[offset + i]);
                Emit(prev, cur, color);
                prev = cur;
            }
        }

        /// <summary>同上。SDK が渡してくる点列は List なので、配列版と別に持つ。</summary>
        internal static void AddLineStrip(List<Vector3> points, int offset, int count, Color color, Matrix4x4 matrix)
        {
            if (count < 2) return;

            _primitiveCount += count - 1;
            _vertexCount += (count - 1) * 2;

            // Handles.matrix が単位行列のことは多い。その場合は変換を丸ごと飛ばす。
            bool identity = matrix.isIdentity;

            Vector3 prev = identity ? points[offset] : matrix.MultiplyPoint3x4(points[offset]);
            for (int i = 1; i < count; i++)
            {
                Vector3 cur = identity ? points[offset + i] : matrix.MultiplyPoint3x4(points[offset + i]);
                Emit(prev, cur, color);
                prev = cur;
            }
        }

        private static void Emit(Vector3 a, Vector3 b, Color color)
        {
            if (Sink != null) { Sink.Line(a, b, color); return; }
            Lines.Add(a, color);
            Lines.Add(b, color);
        }

        /// <summary>三角形（塗り）。</summary>
        internal static void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            _primitiveCount++;
            _vertexCount += 3;

            if (Sink != null) { Sink.Triangle(a, b, c, color); return; }

            Triangles.Add(a, color);
            Triangles.Add(b, color);
            Triangles.Add(c, color);
        }
    }

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
