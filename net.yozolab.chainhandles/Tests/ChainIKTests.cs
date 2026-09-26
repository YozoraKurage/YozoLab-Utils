using NUnit.Framework;
using UnityEngine;
using YozoLab.ChainHandles;

namespace YozoLab.Tests
{
    /// <summary>
    /// 掴んだ関節を届かせる IK。届くこと、長さが変わらないこと、親も曲がること、
    /// 掴んだ関節より先は形を保って運ばれることを確かめる。
    /// </summary>
    public class ChainIKTests
    {
        private const float Epsilon = 1e-4f;

        private static void MakeChain(int bones, bool curl, out Vector3[] positions, out Quaternion[] rotations)
        {
            positions = new Vector3[bones + 1];
            rotations = new Quaternion[bones + 1];
            for (int i = 0; i <= bones; i++)
            {
                positions[i] = curl
                    ? new Vector3(Mathf.Sin(i * 0.4f) * 0.05f, i * 0.1f, 0f)
                    : new Vector3(0f, i * 0.1f, 0f);
                rotations[i] = Quaternion.AngleAxis(i * 11f, Vector3.up);
            }
        }

        private static void AssertLengthsKept(Vector3[] rest, Vector3[] solved)
        {
            for (int i = 0; i + 1 < rest.Length; i++)
            {
                Assert.AreEqual(
                    Vector3.Distance(rest[i], rest[i + 1]),
                    Vector3.Distance(solved[i], solved[i + 1]),
                    Epsilon, $"bone {i}");
            }
        }

        /// <summary>
        /// 回転と位置が食い違っていないか。書き込むのは回転だけなので、
        /// 各ボーンを解いた回転で運び直すと、解いた位置に着く必要がある。
        /// </summary>
        private static void AssertRotationsReachPositions(
            Vector3[] rest, Quaternion[] restRot, Vector3[] positions, Quaternion[] rotations)
        {
            for (int i = 0; i + 1 < rest.Length; i++)
            {
                Vector3 local = Quaternion.Inverse(restRot[i]) * (rest[i + 1] - rest[i]);
                Assert.Less(Vector3.Distance(positions[i] + rotations[i] * local, positions[i + 1]), Epsilon, $"joint {i + 1}");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReachableTarget_IsReachedWithLengthsKept(bool curl)
        {
            MakeChain(8, curl, out Vector3[] rest, out Quaternion[] restRot);
            var p = (Vector3[])rest.Clone();
            var r = (Quaternion[])restRot.Clone();

            var target = new Vector3(0.3f, 0.55f, 0.2f);
            float error = ChainIK.Solve(p, r, 8, target);

            Assert.Less(error, 1e-3f);
            Assert.Less(Vector3.Distance(p[8], target), 1e-3f);
            Assert.Less(Vector3.Distance(p[0], rest[0]), Epsilon, "root must not move");
            AssertLengthsKept(rest, p);
            AssertRotationsReachPositions(rest, restRot, p, r);
        }

        [Test]
        public void MovingTheTip_BendsTheParentsToo()
        {
            // 先端を動かすと、根元に近い関節も回る（先端付近だけが折れるのではない）。
            MakeChain(8, false, out Vector3[] rest, out Quaternion[] restRot);
            var p = (Vector3[])rest.Clone();
            var r = (Quaternion[])restRot.Clone();

            ChainIK.Solve(p, r, 8, new Vector3(0.4f, 0.6f, 0f));

            // 元は真っ直ぐなので、各関節の折れ角がそのまま曲がった量。
            for (int i = 1; i < 8; i++)
            {
                float bend = Vector3.Angle(p[i] - p[i - 1], p[i + 1] - p[i]);
                Assert.Greater(bend, 2f, $"joint {i} should bend");
            }
        }

        [Test]
        public void MiddleJoint_CarriesTheRestRigidly()
        {
            // 途中の関節を掴んだら、その先はローカルの形を保ったまま付いてくる。
            MakeChain(8, true, out Vector3[] rest, out Quaternion[] restRot);
            var p = (Vector3[])rest.Clone();
            var r = (Quaternion[])restRot.Clone();

            var target = new Vector3(0.15f, 0.3f, 0.05f);
            ChainIK.Solve(p, r, 4, target);

            Assert.Less(Vector3.Distance(p[4], target), 1e-3f);
            AssertLengthsKept(rest, p);

            Quaternion carried = r[4] * Quaternion.Inverse(restRot[4]);
            for (int i = 5; i <= 8; i++)
            {
                Quaternion local = Quaternion.Inverse(r[i - 1]) * r[i];
                Quaternion restLocal = Quaternion.Inverse(restRot[i - 1]) * restRot[i];
                Assert.Less(Quaternion.Angle(local, restLocal), 0.05f, $"joint {i} local rotation");

                Vector3 expected = p[4] + carried * (rest[i] - rest[4]);
                Assert.Less(Vector3.Distance(expected, p[i]), Epsilon, $"joint {i} position");
            }
        }

        [Test]
        public void UnreachableTarget_StopsAsCloseAsPossible()
        {
            MakeChain(8, false, out Vector3[] rest, out Quaternion[] restRot);
            var p = (Vector3[])rest.Clone();
            var r = (Quaternion[])restRot.Clone();

            var target = new Vector3(3f, 0.2f, 0f);
            float error = ChainIK.Solve(p, r, 8, target);

            // 鎖の長さは 0.8。根元から 3 ほど先なので、最善でも 2.2 ほど残る。
            float best = Vector3.Distance(rest[0], target) - 0.8f;
            Assert.AreEqual(best, error, 0.02f);
            AssertLengthsKept(rest, p);
            AssertRotationsReachPositions(rest, restRot, p, r);
        }

        [Test]
        public void StraightChainPulledAlongItself_StillBends()
        {
            // 真っ直ぐな鎖を軸に沿って縮める向き。1 次ではどの関節も効かない特異な形。
            MakeChain(8, false, out Vector3[] rest, out Quaternion[] restRot);
            var p = (Vector3[])rest.Clone();
            var r = (Quaternion[])restRot.Clone();

            var target = new Vector3(0f, 0.4f, 0f);
            Assert.Less(ChainIK.Solve(p, r, 8, target), 1e-3f);
            AssertLengthsKept(rest, p);
        }

        [Test]
        public void TargetAtTip_ChangesNothing()
        {
            MakeChain(8, true, out Vector3[] rest, out Quaternion[] restRot);
            var p = (Vector3[])rest.Clone();
            var r = (Quaternion[])restRot.Clone();

            ChainIK.Solve(p, r, 8, rest[8]);

            for (int i = 0; i < rest.Length; i++)
            {
                Assert.Less(Vector3.Distance(rest[i], p[i]), Epsilon);
                Assert.Less(Quaternion.Angle(restRot[i], r[i]), 0.05f);
            }
        }

        [Test]
        public void First_LeavesEarlierJointsAlone()
        {
            // first より根元側は回さない（途中から先だけを解く）。
            MakeChain(8, true, out Vector3[] rest, out Quaternion[] restRot);
            var p = (Vector3[])rest.Clone();
            var r = (Quaternion[])restRot.Clone();

            var target = rest[8] + new Vector3(0.1f, -0.05f, 0.05f);
            Assert.Less(ChainIK.Solve(p, r, 8, target, ChainIK.DefaultBias, 4), 1e-3f);

            for (int i = 0; i <= 4; i++)
                Assert.Less(Vector3.Distance(rest[i], p[i]), Epsilon, $"joint {i} should stay");
            for (int i = 0; i < 4; i++)
                Assert.Less(Quaternion.Angle(restRot[i], r[i]), 0.05f, $"joint {i} should not turn");
            AssertLengthsKept(rest, p);
        }

        /// <summary>ジグザグの鎖。区間ごとに余裕があるので、途中を動かしても先の点に届き直せる。</summary>
        private static void MakeZigZag(out Vector3[] positions, out Quaternion[] rotations)
        {
            positions = new Vector3[9];
            rotations = new Quaternion[9];
            for (int i = 0; i <= 8; i++)
            {
                positions[i] = new Vector3(i % 2 == 0 ? 0f : 0.06f, i * 0.08f, 0f);
                rotations[i] = Quaternion.AngleAxis(i * 7f, Vector3.forward);
            }
        }

        [Test]
        public void KeepingRest_PinsTheHandlesAfterTheGrabbedOne()
        {
            MakeZigZag(out Vector3[] rest, out Quaternion[] restRot);
            var p = (Vector3[])rest.Clone();
            var r = (Quaternion[])restRot.Clone();
            int[] handles = { 0, 3, 6, 8 };

            var target = rest[3] + new Vector3(0.05f, 0.02f, 0.03f);
            ChainIK.SolveKeepingRest(p, r, handles, 1, target, rest, restRot);

            Assert.Less(Vector3.Distance(p[3], target), 1e-3f, "grabbed joint reaches");
            Assert.Less(Vector3.Distance(p[6], rest[6]), 1e-3f, "next handle stays");
            Assert.Less(Vector3.Distance(p[8], rest[8]), 1e-3f, "tip stays");
            Assert.Less(Quaternion.Angle(r[8], restRot[8]), 0.05f, "tip keeps its orientation");
            AssertLengthsKept(rest, p);
            AssertRotationsReachPositions(rest, restRot, p, r);
        }

        [Test]
        public void KeepingRest_OnTheTipIsPlainIK()
        {
            MakeZigZag(out Vector3[] rest, out Quaternion[] restRot);
            var a = (Vector3[])rest.Clone();
            var ar = (Quaternion[])restRot.Clone();
            var b = (Vector3[])rest.Clone();
            var br = (Quaternion[])restRot.Clone();
            var target = rest[8] + new Vector3(0.1f, -0.1f, 0.05f);

            ChainIK.SolveKeepingRest(a, ar, new[] { 0, 4, 8 }, 2, target, rest, restRot);
            ChainIK.Solve(b, br, 8, target);

            for (int i = 0; i < rest.Length; i++)
                Assert.Less(Vector3.Distance(a[i], b[i]), Epsilon);
        }

        [Test]
        public void KeepingRest_UnreachablePinStillKeepsLengths()
        {
            // 真っ直ぐな鎖の途中を横へ引くと、先の区間は元の位置に届かない。長さは守って近づくだけ。
            MakeChain(8, false, out Vector3[] rest, out Quaternion[] restRot);
            var p = (Vector3[])rest.Clone();
            var r = (Quaternion[])restRot.Clone();

            ChainIK.SolveKeepingRest(p, r, new[] { 0, 4, 8 }, 1, rest[4] + new Vector3(0.1f, 0f, 0f), rest, restRot);

            AssertLengthsKept(rest, p);
            Assert.Less(Vector3.Distance(p[8], rest[8]), 0.1f, "tip stays near where it was");
        }

        [Test]
        public void Bias_ShiftsBendTowardRootOrTip()
        {
            // bias 0 は根元寄り、-1.5 は先端寄り。根元の曲がりと先端の曲がりの比で見る。
            float RootToTipRatio(float bias)
            {
                MakeChain(8, false, out Vector3[] rest, out Quaternion[] restRot);
                var p = (Vector3[])rest.Clone();
                var r = (Quaternion[])restRot.Clone();
                ChainIK.Solve(p, r, 8, new Vector3(0.4f, 0.6f, 0f), bias);
                float root = Vector3.Angle(p[2] - p[1], p[1] - p[0]) + Vector3.Angle(p[3] - p[2], p[2] - p[1]);
                float tip = Vector3.Angle(p[7] - p[6], p[6] - p[5]) + Vector3.Angle(p[8] - p[7], p[7] - p[6]);
                return root / tip;
            }

            float rootHeavy = RootToTipRatio(0f);
            float even = RootToTipRatio(ChainIK.DefaultBias);
            float tipHeavy = RootToTipRatio(-1.5f);
            Assert.Greater(rootHeavy, even);
            Assert.Greater(even, tipHeavy);
        }
    }
}
