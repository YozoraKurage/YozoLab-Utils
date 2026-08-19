using UnityEngine;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// 代替ギズモパスの描き込み先。
    ///
    /// パスは PhysBone ごとに拡張と既定形状を呼び、ここへ図形を溜めさせる。
    /// 溜まった頂点は最後に 1 つのメッシュとして一括で描かれるので、
    /// 呼ぶ側は SetPass や発行回数を気にしなくてよい。座標は全てワールド空間。
    ///
    /// 他のエディタ拡張から触れる面（public）はこのクラスと
    /// <see cref="IPhysBoneGizmoExtension"/> だけにしてある。
    /// </summary>
    public sealed class PhysBoneGizmoCanvas
    {
        /// <summary>円 1 周の分割数。SDK の球リング（25 点 = 24 分割）に合わせる。</summary>
        public const int DiscSegments = 24;

        internal readonly MeshBuffer Lines = new MeshBuffer(MeshTopology.Lines);
        internal readonly MeshBuffer Triangles = new MeshBuffer(MeshTopology.Triangles);

        /// <summary>今組み立て中の PhysBone（VRCPhysBoneBase）。</summary>
        public Component PhysBone { get; private set; }

        /// <summary>その PhysBone の GameObject が選択されているか（SDK と同じ意味）。</summary>
        public bool Selected { get; private set; }

        /// <summary>
        /// true にすると、今の PhysBone の既定形状（ボーン線と半径）を描かない。
        /// 拡張が自前の表示で置き換えたいときに使う。PhysBone ごとにリセットされる。
        /// </summary>
        public bool SuppressDefault { get; set; }

        internal int VertexCount => Lines.VertexCount + Triangles.VertexCount;

        /// <summary>テストから図形を覗くための差し込み口。設定中はメッシュへ溜めない。</summary>
        internal interface ISink
        {
            void Line(Vector3 a, Vector3 b, Color color);
            void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color);
        }

        internal ISink Sink;

        internal void Clear()
        {
            Lines.Clear();
            Triangles.Clear();
            PhysBone = null;
        }

        internal void BeginComponent(Component physBone, bool selected)
        {
            PhysBone = physBone;
            Selected = selected;
            SuppressDefault = false;
        }

        // ---------------------------------------------------------------
        // 図形
        // ---------------------------------------------------------------

        public void AddLine(Vector3 a, Vector3 b, Color color)
        {
            if (Sink != null) { Sink.Line(a, b, color); return; }
            Lines.Add(a, color);
            Lines.Add(b, color);
        }

        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            if (Sink != null) { Sink.Triangle(a, b, c, color); return; }
            Triangles.Add(a, color);
            Triangles.Add(b, color);
            Triangles.Add(c, color);
        }

        /// <summary>
        /// 円弧。<paramref name="from"/> を <paramref name="normal"/> の周りに
        /// <paramref name="angle"/> 度回した弧を描く（Quaternion.AngleAxis と同じ回転方向）。
        /// </summary>
        public void AddWireArc(Vector3 center, Vector3 normal, Vector3 from, float angle, float radius, Color color)
        {
            int segments = Mathf.Max(1, Mathf.CeilToInt(DiscSegments * Mathf.Abs(angle) / 360f));
            Vector3 dir = from.normalized * radius;

            Vector3 prev = center + dir;
            for (int i = 1; i <= segments; i++)
            {
                Vector3 cur = center + Quaternion.AngleAxis(angle * i / segments, normal) * dir;
                AddLine(prev, cur, color);
                prev = cur;
            }
        }

        /// <summary>
        /// 塗りの円弧（中心からの三角ファン）。Unity の Handles.DrawSolidArc と同じく
        /// <paramref name="from"/> は正規化するだけで平面へ投影しない。normal と
        /// 直交しない from を渡すと、掃引が円錐の側面になる（SDK のコーン表示が
        /// これを利用している）。
        /// </summary>
        public void AddSolidArc(Vector3 center, Vector3 normal, Vector3 from, float angle, float radius, Color color)
        {
            int segments = Mathf.Max(1, Mathf.CeilToInt(DiscSegments * Mathf.Abs(angle) / 360f));
            Vector3 dir = from.normalized * radius;

            Vector3 prev = center + dir;
            for (int i = 1; i <= segments; i++)
            {
                Vector3 cur = center + Quaternion.AngleAxis(angle * i / segments, normal) * dir;
                AddTriangle(center, prev, cur, color);
                prev = cur;
            }
        }

        public void AddWireDisc(Vector3 center, Vector3 normal, float radius, Color color)
        {
            // from は normal に直交していれば何でもよい
            Vector3 from = Vector3.Cross(normal, Vector3.up);
            if (from.sqrMagnitude < 1e-6f) from = Vector3.Cross(normal, Vector3.right);
            AddWireArc(center, normal, from, 360f, radius, color);
        }

        /// <summary>ワイヤ球。SDK の DrawSphereBatched と同じく直交 3 リング。</summary>
        public void AddWireSphere(Vector3 center, Quaternion rotation, float radius, Color color)
        {
            AddWireArc(center, rotation * Vector3.up, rotation * Vector3.right, 360f, radius, color);
            AddWireArc(center, rotation * Vector3.right, rotation * Vector3.forward, 360f, radius, color);
            AddWireArc(center, rotation * Vector3.forward, rotation * Vector3.right, 360f, radius, color);
        }

        /// <summary>
        /// 先細りワイヤカプセル。PhysBone の半径ギズモと同じ形。
        /// <paramref name="rotation"/> は up 軸が start→end を向く回転。
        /// </summary>
        public void AddTaperedCapsule(
            Vector3 start, Vector3 end, Quaternion rotation,
            float startRadius, float endRadius, Color color)
        {
            Vector3 axis = rotation * Vector3.up;
            Vector3 right = rotation * Vector3.right;
            Vector3 forward = rotation * Vector3.forward;

            // 両端の輪
            AddWireArc(start, axis, right, 360f, startRadius, color);
            AddWireArc(end, axis, right, 360f, endRadius, color);

            // 側面 4 本
            AddLine(start + right * startRadius, end + right * endRadius, color);
            AddLine(start - right * startRadius, end - right * endRadius, color);
            AddLine(start + forward * startRadius, end + forward * endRadius, color);
            AddLine(start - forward * startRadius, end - forward * endRadius, color);

            // 端の丸み（半円 × 直交 2 面）。AngleAxis の回転方向から、
            // end 側は +axis へ、start 側は -axis へ膨らむ組み合わせを選ぶ。
            AddWireArc(end, forward, right, 180f, endRadius, color);
            AddWireArc(end, -right, forward, 180f, endRadius, color);
            AddWireArc(start, -forward, right, 180f, startRadius, color);
            AddWireArc(start, right, forward, 180f, startRadius, color);
        }
    }
}
