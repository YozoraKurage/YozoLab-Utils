using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using YozoLab.VRCGizmoAccelerator;

namespace YozoLab.Tests
{
    /// <summary>
    /// 代替ギズモパスの、シーンに依存しない部分のテスト。
    ///
    /// 実際の描画は GL へ流すのでテストからは見えない。そのため
    /// PhysBoneGizmoCanvas.Sink を差し込み、流れてくる図形を記録して
    /// 「どんな形が何本の線になるか」を検証する。
    /// </summary>
    public class VRCGizmoAcceleratorTests
    {
        /// <summary>図形を記録するだけの受け皿。</summary>
        private sealed class RecordingSink : PhysBoneGizmoCanvas.ISink
        {
            public readonly List<(Vector3 a, Vector3 b, Color color)> Lines =
                new List<(Vector3, Vector3, Color)>();

            public readonly List<(Vector3 a, Vector3 b, Vector3 c, Color color)> Triangles =
                new List<(Vector3, Vector3, Vector3, Color)>();

            public void Line(Vector3 a, Vector3 b, Color color) => Lines.Add((a, b, color));

            public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color) =>
                Triangles.Add((a, b, c, color));
        }

        private PhysBoneGizmoCanvas _canvas;
        private RecordingSink _sink;

        [SetUp]
        public void SetUp()
        {
            _canvas = new PhysBoneGizmoCanvas();
            _sink = new RecordingSink();
            _canvas.Sink = _sink;
        }

        // ---- キャンバスの図形 --------------------------------------------------

        [Test]
        public void WireDisc_EmitsOneSegmentPerDivision()
        {
            _canvas.AddWireDisc(Vector3.zero, Vector3.up, 1f, Color.white);
            Assert.AreEqual(PhysBoneGizmoCanvas.DiscSegments, _sink.Lines.Count);
        }

        [Test]
        public void WireArc_StartsAndEndsOnTheArc()
        {
            var center = new Vector3(1f, 2f, 3f);
            _canvas.AddWireArc(center, Vector3.up, Vector3.right, 180f, 2f, Color.white);

            // 始点は from × 半径、終点は反対側
            Assert.That(Vector3.Distance(_sink.Lines[0].a, center + Vector3.right * 2f),
                Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(_sink.Lines[_sink.Lines.Count - 1].b, center - Vector3.right * 2f),
                Is.LessThan(1e-4f));
        }

        [Test]
        public void WireSphere_IsThreeOrthogonalRings()
        {
            _canvas.AddWireSphere(Vector3.one, Quaternion.identity, 0.5f, Color.white);
            Assert.AreEqual(3 * PhysBoneGizmoCanvas.DiscSegments, _sink.Lines.Count);

            // 全ての頂点が球面上にある
            foreach (var line in _sink.Lines)
            {
                Assert.That(Vector3.Distance(line.a, Vector3.one), Is.EqualTo(0.5f).Within(1e-4f));
                Assert.That(Vector3.Distance(line.b, Vector3.one), Is.EqualTo(0.5f).Within(1e-4f));
            }
        }

        [Test]
        public void TaperedCapsule_VerticesSitOnTheirEndRadii()
        {
            var start = Vector3.zero;
            var end = Vector3.up * 2f;

            _canvas.AddTaperedCapsule(start, end, Quaternion.identity, 0.3f, 0.1f, Color.white);

            // 輪 2 つ + 側面 4 本 + 端の半円 4 つ
            int expected = 2 * PhysBoneGizmoCanvas.DiscSegments + 4
                           + 4 * (PhysBoneGizmoCanvas.DiscSegments / 2);
            Assert.AreEqual(expected, _sink.Lines.Count);

            // どの頂点も「start から 0.3」または「end から 0.1」の球面上、
            // もしくはそれらを結ぶ側面上にある。端の丸みが軸方向へ膨らんでいることを、
            // 頂点の高さの範囲で確かめる（start 側は -0.3、end 側は 2 + 0.1 まで）。
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var line in _sink.Lines)
            {
                minY = Mathf.Min(minY, line.a.y, line.b.y);
                maxY = Mathf.Max(maxY, line.a.y, line.b.y);
            }
            Assert.That(minY, Is.EqualTo(-0.3f).Within(1e-3f), "start 側の丸みが -軸へ膨らむ");
            Assert.That(maxY, Is.EqualTo(2.1f).Within(1e-3f), "end 側の丸みが +軸へ膨らむ");
        }

        [Test]
        public void BeginComponent_ResetsSuppressDefault()
        {
            var go = new GameObject("pb");
            try
            {
                var component = go.transform;

                _canvas.BeginComponent(component, true);
                _canvas.SuppressDefault = true;

                _canvas.BeginComponent(component, false);
                Assert.IsFalse(_canvas.SuppressDefault, "PhysBone ごとにリセットされる");
                Assert.IsFalse(_canvas.Selected);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SolidArc_EmitsOneFanTrianglePerSegment()
        {
            _canvas.AddSolidArc(Vector3.zero, Vector3.up, Vector3.right, 90f, 1f, Color.cyan);

            int segments = Mathf.CeilToInt(PhysBoneGizmoCanvas.DiscSegments * 90f / 360f);
            Assert.AreEqual(segments, _sink.Triangles.Count);

            // ファンの根元は必ず中心
            foreach (var triangle in _sink.Triangles)
                Assert.AreEqual(Vector3.zero, triangle.a);

            // 始端は from × 半径、終端は 90 度回した先
            Assert.That(Vector3.Distance(_sink.Triangles[0].b, Vector3.right), Is.LessThan(1e-4f));
            var last = _sink.Triangles[_sink.Triangles.Count - 1].c;
            Assert.That(Vector3.Distance(last, Quaternion.AngleAxis(90f, Vector3.up) * Vector3.right),
                Is.LessThan(1e-4f));
        }

        /// <summary>
        /// Handles.DrawSolidArc と同じく、from を平面へ投影しないこと。
        /// SDK のコーン表示（normal と直交しない from の 360° 掃引）が
        /// これに依存している。
        /// </summary>
        [Test]
        public void SolidArc_DoesNotProjectFromOntoThePlane()
        {
            // normal=up に対して 45 度傾いた from。掃引はコーンの側面になる
            Vector3 from = (Vector3.up + Vector3.right).normalized;
            _canvas.AddSolidArc(Vector3.zero, Vector3.up, from, 360f, 1f, Color.cyan);

            foreach (var triangle in _sink.Triangles)
            {
                // 全ての外周点が「上向き成分 sin45°」を保つ = 円錐面上
                Assert.That(triangle.b.y, Is.EqualTo(from.y).Within(1e-4f));
                Assert.That(triangle.c.y, Is.EqualTo(from.y).Within(1e-4f));
            }
        }

        // ---- MeshBuffer --------------------------------------------------------

        [Test]
        public void MeshBuffer_CountsVertices()
        {
            _canvas.Sink = null;
            _canvas.AddLine(Vector3.zero, Vector3.one, Color.red);
            _canvas.AddTriangle(Vector3.zero, Vector3.up, Vector3.right, Color.blue);

            Assert.AreEqual(2, _canvas.Lines.VertexCount);
            Assert.AreEqual(3, _canvas.Triangles.VertexCount);
            Assert.AreEqual(5, _canvas.VertexCount);
        }

        /// <summary>
        /// Release 後は配列長が 0 になる。「倍にする」だけの伸長だと 0 のまま
        /// 無限ループするので、下限が効いていることを確かめる（過去に踏んだ穴）。
        /// </summary>
        [Test]
        public void MeshBuffer_GrowsAgainAfterRelease()
        {
            var buffer = new MeshBuffer(MeshTopology.Lines);
            buffer.Add(Vector3.zero, Color.white);
            buffer.Release();

            buffer.Add(Vector3.one, Color.white);
            Assert.AreEqual(1, buffer.VertexCount);
        }
    }
}
