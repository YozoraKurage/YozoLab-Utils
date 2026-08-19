using NUnit.Framework;
using UnityEngine;
using YozoLab.PBRadiusGizmo;

namespace YozoLab.Tests
{
    /// <summary>
    /// Radius ハンドルの「掴んで引いた結果、インスペクタに何が書かれるか」を確かめる。
    ///
    /// シーン上の見た目は目で見るしか無いが、世界座標の半径と Radius の
    /// 行き来を間違えると、カーブやスケールの掛かった PhysBone でだけ
    /// 静かにずれる。そこだけは数値で押さえておく。
    /// </summary>
    public class PhysBoneRadiusGizmoTests
    {
        [Test]
        public void BaseRadiusFromWorld_DividesByFactor()
        {
            // カーブ 0.5 × スケール 2 の場所で、世界座標 0.3 に見える半径は
            // インスペクタ上の Radius 0.3 に当たる。
            Assert.AreEqual(0.3f, RadiusHandleMath.BaseRadiusFromWorld(0.3f, 1f), 1e-6f);
            Assert.AreEqual(0.15f, RadiusHandleMath.BaseRadiusFromWorld(0.3f, 2f), 1e-6f);
            Assert.AreEqual(0.6f, RadiusHandleMath.BaseRadiusFromWorld(0.3f, 0.5f), 1e-6f);
        }

        [Test]
        public void BaseRadiusFromWorld_NeverGoesNegative()
        {
            Assert.AreEqual(0f, RadiusHandleMath.BaseRadiusFromWorld(-1f, 2f));
        }

        [Test]
        public void BaseRadiusFromWorld_ReturnsZeroWhenFactorIsUnusable()
        {
            // カーブが 0、スケールが 0、といった逆算できない場所。
            Assert.AreEqual(0f, RadiusHandleMath.BaseRadiusFromWorld(0.3f, 0f));
            Assert.AreEqual(0f, RadiusHandleMath.BaseRadiusFromWorld(0.3f, -1f));
            Assert.AreEqual(0f, RadiusHandleMath.BaseRadiusFromWorld(0.3f, float.NaN));
        }

        [Test]
        public void Snap_RoundsToStep_AndPassesThroughWhenStepIsZero()
        {
            Assert.AreEqual(0.123f, RadiusHandleMath.Snap(0.1234f, 0.001f), 1e-6f);
            Assert.AreEqual(0.1234f, RadiusHandleMath.Snap(0.1234f, 0f), 1e-6f);
        }

        [Test]
        public void PickHandleDirection_AvoidsTheAxisAlignedWithTheView()
        {
            // 視線が +z のとき、+z 向きの軸を掴んでも画面上ではほとんど動かない。
            Vector3 direction = RadiusHandleMath.PickHandleDirection(
                Vector3.forward, Vector3.right, Vector3.forward, Vector3.right);

            Assert.AreEqual(Vector3.right, direction);
        }

        [Test]
        public void PickHandleDirection_PointsTowardTheRightOfTheScreen()
        {
            // 「右へ引くと大きくなる」を保つため、符号は画面右に合わせる。
            Vector3 direction = RadiusHandleMath.PickHandleDirection(
                Vector3.right, Vector3.up, Vector3.forward, Vector3.left);

            Assert.AreEqual(Vector3.left, direction);
        }

        [Test]
        public void PickHandleDirection_SurvivesDegenerateAxes()
        {
            Vector3 direction = RadiusHandleMath.PickHandleDirection(
                Vector3.zero, Vector3.zero, Vector3.forward, Vector3.right);

            Assert.AreEqual(Vector3.right, direction);
        }
    }
}
