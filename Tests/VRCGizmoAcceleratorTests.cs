using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YozoLab.VRCGizmoAccelerator;

namespace YozoLab.Tests
{
    /// <summary>
    /// 「元の描画と同じものが出るか」を確かめるテスト。
    ///
    /// 実際の描画は GL へ直接流すのでテストからは見えない。そのため
    /// GizmoBatch.Sink を差し込み、流れてくる図形をそのまま記録して検証する。
    /// </summary>
    public class VRCGizmoAcceleratorTests
    {
        /// <summary>横取りされた図形を記録するだけの受け皿。</summary>
        private sealed class RecordingSink : GizmoBatch.ISink
        {
            public readonly List<(Vector3 a, Vector3 b, Color color)> Lines =
                new List<(Vector3, Vector3, Color)>();

            public readonly List<(Vector3 a, Vector3 b, Vector3 c, Color color)> Triangles =
                new List<(Vector3, Vector3, Vector3, Color)>();

            public void Line(Vector3 a, Vector3 b, Color color) => Lines.Add((a, b, color));

            public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color) =>
                Triangles.Add((a, b, c, color));
        }

        private RecordingSink _sink;

        [SetUp]
        public void SetUp()
        {
            GizmoBatch.Reset();
            _sink = new RecordingSink();
            GizmoBatch.Sink = _sink;
        }

        [TearDown]
        public void TearDown()
        {
            GizmoBatch.Sink = null;
            GizmoBatch.Reset();
        }

        // ---- 形の互換性 -------------------------------------------------------

        /// <summary>
        /// 円弧の点列が Handles 本体の内部実装と一致すること。
        /// ここがずれると円弧の形が変わる = 互換性が崩れる。
        /// </summary>
        [Test]
        public void DiscSectionPoints_MatchUnityImplementation()
        {
            var unityMethod = typeof(Handles).GetMethod(
                "SetDiscSectionPoints",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                new[]
                {
                    typeof(Vector3[]), typeof(Vector3), typeof(Vector3), typeof(Vector3),
                    typeof(float), typeof(float),
                },
                null);

            if (unityMethod == null)
            {
                Assert.Ignore("この Unity には Handles.SetDiscSectionPoints が無い（比較できない）");
            }

            var center = new Vector3(1f, 2f, -3f);
            var normal = new Vector3(0.3f, 1f, 0.2f).normalized;
            var from = new Vector3(1f, 0.5f, 0f);
            const float angle = 137.5f;
            const float radius = 0.42f;

            var expected = new Vector3[60];
            unityMethod.Invoke(null, new object[] { expected, center, normal, from, angle, radius });

            var actual = new Vector3[60];
            HandlesInterceptor.SetDiscSectionPoints(actual, center, normal, from, angle, radius);

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(Vector3.Distance(expected[i], actual[i]), Is.LessThan(1e-4f),
                    $"点 {i} がずれている: 期待 {expected[i]} / 実際 {actual[i]}");
            }
        }

        /// <summary>
        /// 円弧の分割数が Handles と同じであること。Unity 側は固定 60 点
        /// (Handles.kArcSegments) で、ここがずれると滑らかさが元と変わる。
        /// </summary>
        [Test]
        public void ArcSegmentCount_MatchesUnity()
        {
            var field = typeof(Handles).GetField("kArcSegments",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            if (field == null) Assert.Ignore("この Unity には Handles.kArcSegments が無い");

            Assert.AreEqual((int)field.GetValue(null), HandlesInterceptor.ArcPointCount);
        }

        [Test]
        public void WireArc_EmitsOneSegmentPerGap()
        {
            HandlesInterceptor.GizmoScope_Prefix(null);
            try
            {
                HandlesInterceptor.DrawWireArc_Prefix(Vector3.zero, Vector3.up, Vector3.right, 90f, 1f);
            }
            finally { GizmoBatch.Reset(); }

            Assert.AreEqual(59, _sink.Lines.Count, "60 点の連続線 = 59 本の線分");

            // 隣り合う線分が繋がっていること（連続線として描かれていること）
            for (int i = 1; i < _sink.Lines.Count; i++)
            {
                Assert.AreEqual(_sink.Lines[i - 1].b, _sink.Lines[i].a);
            }
        }

        [Test]
        public void LineStrip_AppliesMatrix()
        {
            var points = new List<Vector3> { Vector3.zero, Vector3.right };
            var matrix = Matrix4x4.TRS(new Vector3(0f, 5f, 0f), Quaternion.identity, Vector3.one * 2f);

            GizmoBatch.AddLineStrip(points, 0, 2, Color.white, matrix);

            Assert.AreEqual(1, _sink.Lines.Count);
            Assert.AreEqual(new Vector3(0f, 5f, 0f), _sink.Lines[0].a);
            Assert.AreEqual(new Vector3(2f, 5f, 0f), _sink.Lines[0].b);
        }

        [Test]
        public void SolidArc_EmitsFanFromCenter()
        {
            var center = new Vector3(1f, 0f, 0f);

            HandlesInterceptor.GizmoScope_Prefix(null);
            try
            {
                HandlesInterceptor.DrawSolidArc_Prefix(center, Vector3.up, Vector3.right, 90f, 1f);
            }
            finally { GizmoBatch.Reset(); }

            Assert.AreEqual(59, _sink.Triangles.Count, "60 点の扇 = 59 枚");
            Assert.IsTrue(_sink.Triangles.All(t => t.a == center), "全ての三角形が中心を共有する");
        }

        // ---- 区間の扱い -------------------------------------------------------

        /// <summary>区間の外では横取りしない（他ツールの Handles 呼び出しを壊さない）。</summary>
        [Test]
        public void OutsideScope_PrefixesLetOriginalRun()
        {
            Assert.AreEqual(GizmoBatch.Interception.PassThrough, GizmoBatch.Mode);

            Assert.IsTrue(HandlesInterceptor.DrawLine_Prefix(Vector3.zero, Vector3.one),
                "区間外では元の実装を走らせるべき");
            Assert.IsTrue(HandlesInterceptor.DrawWireDisc_Prefix(Vector3.zero, Vector3.up, 1f));
            Assert.IsTrue(HandlesInterceptor.ApplyWireMaterial_Prefix());
            Assert.IsEmpty(_sink.Lines);
        }

        [Test]
        public void InsideScope_PrefixesTakeOver()
        {
            HandlesInterceptor.GizmoScope_Prefix(null);
            try
            {
                Assert.AreEqual(GizmoBatch.Interception.Capture, GizmoBatch.Mode);
                Assert.IsFalse(HandlesInterceptor.DrawLine_Prefix(Vector3.zero, Vector3.one),
                    "区間内では元の実装を止めるべき");
                Assert.IsFalse(HandlesInterceptor.ApplyWireMaterial_Prefix(),
                    "マテリアルの適用は区間に 1 回だけにするので、個別の呼び出しは止める");
                Assert.AreEqual(1, _sink.Lines.Count);
            }
            finally { GizmoBatch.Reset(); }
        }

        /// <summary>
        /// OnSceneGUI は Layout など描画以外のイベントでも呼ばれる。
        /// そこで横取りすると、当たり判定用のイベントで無駄な処理をすることになる。
        /// </summary>
        [Test]
        public void OnlyDrawingEventsAreIntercepted()
        {
            // ギズモのコールバックは GUI の外から呼ばれる（Event.current が null）
            Assert.IsTrue(GizmoBatch.IsDrawingEvent(null));
            Assert.IsTrue(GizmoBatch.IsDrawingEvent(new Event { type = EventType.Repaint }));

            Assert.IsFalse(GizmoBatch.IsDrawingEvent(new Event { type = EventType.Layout }),
                "Layout は当たり判定の登録なので横取りしない");
            Assert.IsFalse(GizmoBatch.IsDrawingEvent(new Event { type = EventType.MouseMove }));
            Assert.IsFalse(GizmoBatch.IsDrawingEvent(new Event { type = EventType.MouseDrag }));
        }

        // ---- SDK の点列の食い方 ----------------------------------------------

        /// <summary>
        /// 球は 25 点リング × 3。SDK 側の食い方と 1 点でもずれると、
        /// 以降の図形が全部ずれる。返り値は次の offset。
        /// </summary>
        [Test]
        public void SphereBatched_ConsumesThreeRingsOf25()
        {
            var buffer = Enumerable.Range(0, 75).Select(i => new Vector3(i, 0f, 0f)).ToList();
            int result = 0;

            HandlesInterceptor.GizmoScope_Prefix(null);
            try
            {
                Assert.IsFalse(HandlesInterceptor.DrawSphereBatched_Prefix(
                    ref buffer, 0, Color.white, Matrix4x4.identity, ref result));
            }
            finally { GizmoBatch.Reset(); }

            Assert.AreEqual(75, result, "球は 75 点を消費して次の offset を返す");
            Assert.AreEqual(24 * 3, _sink.Lines.Count, "25 点の連続線 × 3 = 72 本");
        }

        [Test]
        public void CapsuleBatched_Consumes110Points()
        {
            var buffer = Enumerable.Range(0, 110).Select(i => new Vector3(i, 0f, 0f)).ToList();
            int result = 0;

            HandlesInterceptor.GizmoScope_Prefix(null);
            try
            {
                HandlesInterceptor.DrawCapsuleBatched_Prefix(
                    ref buffer, 0, Color.white, Matrix4x4.identity, ref result);
            }
            finally { GizmoBatch.Reset(); }

            Assert.AreEqual(110, result, "側面 8 + リング 25×2 + キャップ 13×4 = 110 点");
            Assert.AreEqual(4 + 24 * 2 + 12 * 4, _sink.Lines.Count);
        }

        [Test]
        public void LineBatched_ReturnsConsumedCount()
        {
            var buffer = new List<Vector3> { Vector3.zero, Vector3.one };
            int result = 0;

            HandlesInterceptor.GizmoScope_Prefix(null);
            try
            {
                HandlesInterceptor.DrawLineBatched_Prefix(ref buffer, 0, Color.white, ref result);
            }
            finally { GizmoBatch.Reset(); }

            Assert.AreEqual(2, result, "線は消費点数 2 を返す（SDK の規約に合わせる）");
            Assert.AreEqual(1, _sink.Lines.Count);
        }

        /// <summary>
        /// 描かない場合でも、呼び出し側が進めるカーソルは元と同じでなければならない。
        /// </summary>
        [Test]
        public void CursorIsCorrectFromAnyOffset()
        {
            var buffer = Enumerable.Range(0, 200).Select(i => new Vector3(i, 0f, 0f)).ToList();
            int result = 0;

            HandlesInterceptor.GizmoScope_Prefix(null);
            try
            {
                HandlesInterceptor.DrawCapsuleBatched_Prefix(
                    ref buffer, 10, Color.white, Matrix4x4.identity, ref result);
            }
            finally { GizmoBatch.Reset(); }

            Assert.AreEqual(120, result, "10 + 110");
        }

        // ---- ボーン構造のキャッシュ ------------------------------------------

        /// <summary>ギズモ描画の外では、構造の作り直しを絶対に省かない。</summary>
        [Test]
        public void BoneInitCache_OutsideGizmoScope_NeverSkips()
        {
            var dummy = ScriptableObject.CreateInstance<ScriptableObject>();
            try
            {
                PhysBoneInitCache.InvalidateAll();
                Assert.IsTrue(PhysBoneInitCache.InitTransforms_Prefix(dummy, true));
                Assert.IsTrue(PhysBoneInitCache.InitTransforms_Prefix(dummy, true));
            }
            finally { Object.DestroyImmediate(dummy); }
        }

        /// <summary>1 回目は作り直させ、2 回目以降は省く。合図が来たらまた作り直す。</summary>
        [Test]
        public void BoneInitCache_SkipsUntilInvalidated()
        {
            var dummy = ScriptableObject.CreateInstance<ScriptableObject>();
            HandlesInterceptor.GizmoScope_Prefix(null);
            try
            {
                PhysBoneInitCache.InvalidateAll();

                Assert.IsTrue(PhysBoneInitCache.InitTransforms_Prefix(dummy, true),
                    "1 回目は元の作り直しを走らせる");
                Assert.IsFalse(PhysBoneInitCache.InitTransforms_Prefix(dummy, true),
                    "2 回目は省く");

                PhysBoneInitCache.InvalidateAll();

                Assert.IsTrue(PhysBoneInitCache.InitTransforms_Prefix(dummy, true),
                    "変化があったら作り直す");
            }
            finally
            {
                GizmoBatch.Reset();
                PhysBoneInitCache.InvalidateAll();
                Object.DestroyImmediate(dummy);
            }
        }

        /// <summary>force が立っていない呼び出しは SDK 側の早期 return に任せる。</summary>
        [Test]
        public void BoneInitCache_NonForcedCall_IsLeftAlone()
        {
            var dummy = ScriptableObject.CreateInstance<ScriptableObject>();
            HandlesInterceptor.GizmoScope_Prefix(null);
            try
            {
                PhysBoneInitCache.InvalidateAll();
                Assert.IsTrue(PhysBoneInitCache.InitTransforms_Prefix(dummy, false));
                Assert.IsTrue(PhysBoneInitCache.InitTransforms_Prefix(dummy, false));
            }
            finally
            {
                GizmoBatch.Reset();
                PhysBoneInitCache.InvalidateAll();
                Object.DestroyImmediate(dummy);
            }
        }
    }
}
