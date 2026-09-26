using NUnit.Framework;
using UnityEngine;
using YozoLab.ChainHandles;

namespace YozoLab.Tests
{
    /// <summary>
    /// 回転ギズモの 2 通りの戻し方（累積・差分）のどちらでも、取り出した回転を足し合わせると
    /// ギズモで回した量ちょうどになること。以前は Global のとき回転が膨らんでいた。
    /// </summary>
    public class RotationGizmoTrackerTests
    {
        private static readonly Vector3 Axis = new Vector3(0.2f, 1f, 0.3f).normalized;

        /// <summary>Disc と同じ振る舞い: 掴んだ時点の向き start を起点にした累積を返す。</summary>
        private static Quaternion Cumulative(Quaternion start, float totalDegrees) =>
            Quaternion.AngleAxis(totalDegrees, Axis) * start;

        /// <summary>Transform に書いたときと同じく正規化する（Transform は正規化して持つ）。</summary>
        private static Quaternion Store(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
        }

        /// <summary>FreeRotate と同じ振る舞い: 渡された向きに今回の分を掛けて返す。</summary>
        private static Quaternion Incremental(Quaternion passed, float stepDegrees) =>
            Quaternion.AngleAxis(stepDegrees, Axis) * passed;

        [TestCase(true)]
        [TestCase(false)]
        public void CumulativeGizmo_AddsUpToTheDraggedAngle(bool local)
        {
            var tracker = new RotationGizmoTracker();
            Quaternion joint = Quaternion.AngleAxis(30f, Vector3.right);
            Quaternion initial = local ? joint : Quaternion.identity;
            Quaternion start = tracker.Shown(initial);

            for (int frame = 1; frame <= 10; frame++)
            {
                Quaternion shown = tracker.Shown(local ? joint : Quaternion.identity);
                Quaternion returned = Cumulative(start, frame * 5f);
                joint = Store(tracker.Take(shown, returned) * joint);
            }

            Quaternion expected = Quaternion.AngleAxis(50f, Axis) * Quaternion.AngleAxis(30f, Vector3.right);
            Assert.Less(Quaternion.Angle(expected, joint), 0.01f);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void IncrementalGizmo_AddsUpToTheDraggedAngle(bool local)
        {
            var tracker = new RotationGizmoTracker();
            Quaternion joint = Quaternion.AngleAxis(30f, Vector3.right);

            for (int frame = 1; frame <= 10; frame++)
            {
                Quaternion shown = tracker.Shown(local ? joint : Quaternion.identity);
                Quaternion returned = Incremental(shown, 5f);
                joint = Store(tracker.Take(shown, returned) * joint);
            }

            Quaternion expected = Quaternion.AngleAxis(50f, Axis) * Quaternion.AngleAxis(30f, Vector3.right);
            Assert.Less(Quaternion.Angle(expected, joint), 0.01f);
        }

        [Test]
        public void Reset_StartsOverFromInitial()
        {
            var tracker = new RotationGizmoTracker();
            Quaternion shown = tracker.Shown(Quaternion.identity);
            tracker.Take(shown, Quaternion.AngleAxis(40f, Axis));
            tracker.Reset();

            Quaternion initial = Quaternion.AngleAxis(10f, Vector3.up);
            Assert.AreEqual(0f, Quaternion.Angle(initial, tracker.Shown(initial)), 1e-4f);
        }
    }
}
