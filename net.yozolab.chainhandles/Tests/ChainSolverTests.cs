using NUnit.Framework;
using UnityEngine;
using YozoLab.ChainHandles;

namespace YozoLab.Tests
{
    /// <summary>
    /// 鎖の解き方を数値で確かめる。
    ///
    /// このツールの約束は二つ。「掴んだだけでは何も変わらない」ことと、
    /// 「どう動かしてもボーンの長さが変わらない」こと。どちらも目で見て
    /// 気付くのは壊れた後なので、ここで押さえておく。
    /// </summary>
    public class ChainSolverTests
    {
        private const float Epsilon = 1e-4f;

        /// <summary>うねりのある 11 関節の鎖。関節ごとに向きも違う。</summary>
        private static void MakeWavyChain(out Vector3[] positions, out Quaternion[] rotations)
        {
            positions = new Vector3[11];
            rotations = new Quaternion[11];
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = new Vector3(Mathf.Sin(i * 0.7f) * 0.2f, i * 0.3f, Mathf.Cos(i * 0.5f) * 0.1f);
                rotations[i] = Quaternion.AngleAxis(i * 17f, new Vector3(0.3f, 1f, 0.2f).normalized);
            }
        }

        private static float[] Zeros(int count) => new float[count];

        private static void AssertLengthsKept(Vector3[] rest, Vector3[] solved)
        {
            for (int i = 0; i + 1 < rest.Length; i++)
            {
                float expected = Vector3.Distance(rest[i], rest[i + 1]);
                float actual = Vector3.Distance(solved[i], solved[i + 1]);
                Assert.AreEqual(expected, actual, Epsilon, $"bone {i}");
            }
        }

        /// <summary>
        /// 解いた回転で rest の骨を運び直すと、解いた位置にちゃんと届いているか。
        /// 書き込むのは回転だけなので、これが崩れると見た目の位置が解とずれる。
        /// </summary>
        private static void AssertRotationsReachPositions(
            Vector3[] restPositions, Quaternion[] restRotations, Vector3[] positions, Quaternion[] rotations)
        {
            for (int i = 0; i + 1 < restPositions.Length; i++)
            {
                Vector3 local = Quaternion.Inverse(restRotations[i]) * (restPositions[i + 1] - restPositions[i]);
                Vector3 reached = positions[i] + rotations[i] * local;
                Assert.Less(Vector3.Distance(reached, positions[i + 1]), Epsilon, $"joint {i + 1}");
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(5)]
        [TestCase(20)]
        public void UntouchedControls_ReproduceRestPose(int divisions)
        {
            MakeWavyChain(out Vector3[] rest, out Quaternion[] restRot);
            Vector3[] controls = ChainSolver.PlaceControls(rest, divisions, out float[] knots);

            var positions = new Vector3[rest.Length];
            var rotations = new Quaternion[rest.Length];
            Assert.IsTrue(ChainSolver.Solve(
                rest, restRot, controls, (Vector3[])controls.Clone(), Zeros(controls.Length),
                positions, rotations, knots));

            for (int i = 0; i < rest.Length; i++)
            {
                Assert.Less(Vector3.Distance(rest[i], positions[i]), Epsilon, $"position {i}");
                Assert.Less(Quaternion.Angle(restRot[i], rotations[i]), 0.05f, $"rotation {i}");
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(5)]
        public void MovedControls_KeepBoneLengths(int divisions)
        {
            MakeWavyChain(out Vector3[] rest, out Quaternion[] restRot);
            Vector3[] restControls = ChainSolver.PlaceControls(rest, divisions, out float[] knots);
            var controls = (Vector3[])restControls.Clone();
            controls[divisions] += new Vector3(1f, -0.5f, 0.3f);
            if (divisions > 1) controls[1] += new Vector3(-0.3f, 0f, 0.2f);

            var rolls = new float[controls.Length];
            for (int k = 0; k < rolls.Length; k++) rolls[k] = k * 30f;

            var positions = new Vector3[rest.Length];
            var rotations = new Quaternion[rest.Length];
            Assert.IsTrue(ChainSolver.Solve(rest, restRot, restControls, controls, rolls, positions, rotations, knots));

            AssertLengthsKept(rest, positions);
            AssertRotationsReachPositions(rest, restRot, positions, rotations);
            Assert.Less(Vector3.Distance(rest[0], positions[0]), Epsilon, "root must not move");
        }

        [Test]
        public void ControlPulledFarAway_ChainDoesNotStretch()
        {
            MakeWavyChain(out Vector3[] rest, out Quaternion[] restRot);
            Vector3[] restControls = ChainSolver.PlaceControls(rest, 3, out float[] knots);
            var controls = (Vector3[])restControls.Clone();
            controls[3] += new Vector3(0f, 50f, 0f);

            var positions = new Vector3[rest.Length];
            var rotations = new Quaternion[rest.Length];
            ChainSolver.Solve(rest, restRot, restControls, controls, Zeros(4), positions, rotations, knots);

            AssertLengthsKept(rest, positions);
            AssertRotationsReachPositions(rest, restRot, positions, rotations);
        }

        [Test]
        public void ControlPushedBack_ChainRunsPastCurveEnd()
        {
            // 芯が鎖より短くなる向き。末端の先へ延ばして長さを守る。
            MakeWavyChain(out Vector3[] rest, out Quaternion[] restRot);
            Vector3[] restControls = ChainSolver.PlaceControls(rest, 2, out float[] knots);
            var controls = (Vector3[])restControls.Clone();
            controls[2] = Vector3.Lerp(controls[0], controls[2], 0.3f);
            controls[1] = Vector3.Lerp(controls[0], controls[2], 0.5f);

            var positions = new Vector3[rest.Length];
            var rotations = new Quaternion[rest.Length];
            ChainSolver.Solve(rest, restRot, restControls, controls, Zeros(3), positions, rotations, knots);

            AssertLengthsKept(rest, positions);
            AssertRotationsReachPositions(rest, restRot, positions, rotations);
        }

        [Test]
        public void StraightChain_RollOnlyTwists()
        {
            // 芯の上に乗った真っ直ぐな鎖では、ロールは位置を変えず、骨軸まわりにだけ回す。
            var rest = new Vector3[6];
            var restRot = new Quaternion[6];
            for (int i = 0; i < rest.Length; i++)
            {
                rest[i] = new Vector3(0f, i * 0.5f, 0f);
                restRot[i] = Quaternion.identity;
            }

            Vector3[] controls = ChainSolver.PlaceControls(rest, 2, out float[] knots);
            var rolls = new[] { 0f, 0f, 90f };

            var positions = new Vector3[rest.Length];
            var rotations = new Quaternion[rest.Length];
            ChainSolver.Solve(rest, restRot, controls, (Vector3[])controls.Clone(), rolls, positions, rotations, knots);

            for (int i = 0; i < rest.Length; i++)
            {
                Assert.Less(Vector3.Distance(rest[i], positions[i]), Epsilon, $"position {i}");
                Vector3 axis = rotations[i] * Vector3.up;
                Assert.Less(Vector3.Angle(axis, Vector3.up), 0.05f, $"axis {i}");
            }

            // 先端は 90 度、中間の制御点から根元までは捻らない。
            Assert.AreEqual(90f, Quaternion.Angle(restRot[5], rotations[5]), 0.05f);
            Assert.AreEqual(0f, Quaternion.Angle(restRot[1], rotations[1]), 0.05f);
        }

        [Test]
        public void ZeroLengthBones_AreCarriedWithoutBreaking()
        {
            // 同じ位置に重なった関節（ねじれ補助ボーンなど）が混ざっていても解ける。
            var rest = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0.5f, 0f),
                new Vector3(0f, 0.5f, 0f),
                new Vector3(0f, 1f, 0.1f),
                new Vector3(0f, 1.5f, 0.3f),
            };
            var restRot = new Quaternion[rest.Length];
            for (int i = 0; i < restRot.Length; i++) restRot[i] = Quaternion.identity;

            Vector3[] restControls = ChainSolver.PlaceControls(rest, 2, out float[] knots);
            var controls = (Vector3[])restControls.Clone();
            controls[2] += new Vector3(0.8f, -0.4f, 0f);

            var positions = new Vector3[rest.Length];
            var rotations = new Quaternion[rest.Length];
            Assert.IsTrue(ChainSolver.Solve(rest, restRot, restControls, controls, Zeros(3), positions, rotations, knots));

            AssertLengthsKept(rest, positions);
            AssertRotationsReachPositions(rest, restRot, positions, rotations);
            foreach (Quaternion q in rotations)
                Assert.IsFalse(float.IsNaN(q.x) || float.IsNaN(q.w));
        }

        [Test]
        public void DegenerateChain_IsRejected()
        {
            var rest = new[] { Vector3.one, Vector3.one, Vector3.one };
            var restRot = new[] { Quaternion.identity, Quaternion.identity, Quaternion.identity };
            Vector3[] controls = ChainSolver.PlaceControls(rest, 2, out float[] knots);

            var positions = new Vector3[3];
            var rotations = new Quaternion[3];
            Assert.IsFalse(ChainSolver.Solve(rest, restRot, controls, controls, Zeros(3), positions, rotations, knots));
        }

        // ---- 選んだボーンにハンドルを置く場合 --------------------------------

        [Test]
        public void ControlsOnChosenJoints_ReproduceRestPose()
        {
            MakeWavyChain(out Vector3[] rest, out Quaternion[] restRot);
            Vector3[] controls = ChainSolver.PlaceControls(rest, new[] { 0, 2, 3, 7, 10 }, out float[] knots);

            var positions = new Vector3[rest.Length];
            var rotations = new Quaternion[rest.Length];
            Assert.IsTrue(ChainSolver.Solve(
                rest, restRot, controls, (Vector3[])controls.Clone(), Zeros(controls.Length),
                positions, rotations, knots));

            for (int i = 0; i < rest.Length; i++)
            {
                Assert.Less(Vector3.Distance(rest[i], positions[i]), Epsilon, $"position {i}");
                Assert.Less(Quaternion.Angle(restRot[i], rotations[i]), 0.05f, $"rotation {i}");
            }
        }

        [Test]
        public void ControlsOnChosenJoints_KeepBoneLengths()
        {
            MakeWavyChain(out Vector3[] rest, out Quaternion[] restRot);
            Vector3[] restControls = ChainSolver.PlaceControls(rest, new[] { 0, 1, 6, 10 }, out float[] knots);
            var controls = (Vector3[])restControls.Clone();
            controls[1] += new Vector3(0.2f, 0f, -0.1f);
            controls[3] += new Vector3(1f, -0.5f, 0.3f);

            var positions = new Vector3[rest.Length];
            var rotations = new Quaternion[rest.Length];
            Assert.IsTrue(ChainSolver.Solve(
                rest, restRot, restControls, controls, new[] { 0f, 20f, -40f, 90f }, positions, rotations, knots));

            AssertLengthsKept(rest, positions);
            AssertRotationsReachPositions(rest, restRot, positions, rotations);
        }

        [Test]
        public void ChosenJoints_ControlsSitOnThoseJoints()
        {
            MakeWavyChain(out Vector3[] rest, out _);
            int[] indices = { 0, 4, 9, 10 };
            Vector3[] controls = ChainSolver.PlaceControls(rest, indices, out float[] knots);
            float[] jointU = ChainSolver.JointParameters(rest, out _);

            Assert.AreEqual(indices.Length, controls.Length);
            for (int k = 0; k < indices.Length; k++)
            {
                Assert.Less(Vector3.Distance(rest[indices[k]], controls[k]), Epsilon);
                Assert.AreEqual(jointU[indices[k]], knots[k], 1e-6f);

                // 曲線は各ハンドルをそのノットで通る。
                Assert.Less(Vector3.Distance(controls[k], ChainSolver.Spline(controls, knots[k], knots)), Epsilon);
            }
        }

        [Test]
        public void EvenKnots_MatchUniformCurve()
        {
            // 等間隔のノットを明示しても、null（等間隔）と同じ曲線・同じロールになる。
            var c = new[] { Vector3.zero, new Vector3(0f, 1f, 0.3f), new Vector3(0.5f, 2f, 0f), new Vector3(1f, 2.5f, -0.4f) };
            var knots = new[] { 0f, 1f / 3f, 2f / 3f, 1f };
            var rolls = new[] { 0f, 30f, -10f, 45f };

            for (int s = 0; s <= 30; s++)
            {
                float u = s / 30f;
                Assert.Less(Vector3.Distance(ChainSolver.Spline(c, u), ChainSolver.Spline(c, u, knots)), 1e-5f);
                Assert.Less(Vector3.Distance(ChainSolver.SplineTangent(c, u), ChainSolver.SplineTangent(c, u, knots)), 1e-4f);
                Assert.AreEqual(ChainSolver.SampleRoll(rolls, u), ChainSolver.SampleRoll(rolls, u, knots), 1e-4f);
            }
        }

        [Test]
        public void ChosenJoints_OverlappingJointsAreMerged()
        {
            // 長さ 0 のボーンを挟んで重なった 2 つを選んでも、ノットが重ならないよう 1 つにまとめる。
            var joints = new[] { Vector3.zero, Vector3.up, Vector3.up, new Vector3(0f, 2f, 0f) };
            Vector3[] controls = ChainSolver.PlaceControls(joints, new[] { 0, 1, 2, 3 }, out float[] knots);

            Assert.AreEqual(3, controls.Length);
            CollectionAssert.AreEqual(new[] { 0f, 0.5f, 1f }, knots);
        }

        [Test]
        public void EvenJointIndices_NeverExceedsBoneCount()
        {
            // ボーン 2 本の鎖を 4 分割しようとしても、ハンドルは関節の数（3）まで。
            var joints = new[] { Vector3.zero, new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 3f) };
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, ChainSolver.EvenJointIndices(joints, 4));

            Vector3[] controls = ChainSolver.PlaceControls(joints, 4, out _);
            Assert.AreEqual(3, controls.Length);
            for (int i = 0; i < joints.Length; i++)
                Assert.Less(Vector3.Distance(controls[i], joints[i]), Epsilon);
        }

        [Test]
        public void EvenJointIndices_PicksJointsNearEvenArcLength()
        {
            // 1 本目だけ長い鎖。弧長で 2 等分すると、真ん中は長い 1 本目の先に来る。
            var joints = new[]
            {
                Vector3.zero, new Vector3(0f, 3f, 0f), new Vector3(0f, 4f, 0f),
                new Vector3(0f, 5f, 0f), new Vector3(0f, 6f, 0f),
            };
            CollectionAssert.AreEqual(new[] { 0, 1, 4 }, ChainSolver.EvenJointIndices(joints, 2));
        }

        [Test]
        public void EvenJointIndices_KeepsHandlesOnDistinctJoints()
        {
            // 極端に不揃いでも同じ関節を二度選ばず、根元と先端は必ず含む。
            var joints = new[]
            {
                Vector3.zero, new Vector3(0f, 0.01f, 0f), new Vector3(0f, 0.02f, 0f),
                new Vector3(0f, 10f, 0f),
            };
            int[] indices = ChainSolver.EvenJointIndices(joints, 3);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, indices);
        }
    }
}
