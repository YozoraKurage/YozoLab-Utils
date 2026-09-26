using NUnit.Framework;
using UnityEngine;
using YozoLab.ChainHandles;

namespace YozoLab.Tests
{
    /// <summary>
    /// 鎖をアバターからの相対パスで保存し、引き直せること。
    /// 保存した鎖が別のボーンを指してしまうと、気付かないまま違う所を曲げることになる。
    /// </summary>
    public class AvatarRootsTests
    {
        private GameObject _avatar;
        private Transform _hips;
        private Transform _hair1;
        private Transform _hair2;

        [SetUp]
        public void SetUp()
        {
            _avatar = new GameObject("Avatar");
            _avatar.AddComponent<Animator>();
            var armature = new GameObject("Armature").transform;
            armature.SetParent(_avatar.transform);
            _hips = new GameObject("Hips").transform;
            _hips.SetParent(armature);
            _hair1 = new GameObject("Hair_1").transform;
            _hair1.SetParent(_hips);
            _hair2 = new GameObject("Hair_2").transform;
            _hair2.SetParent(_hair1);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_avatar);

        [Test]
        public void PathRoundTrips()
        {
            Transform root = _avatar.transform;
            string path = AvatarRoots.PathFrom(root, _hair2);

            Assert.AreEqual("Armature/Hips/Hair_1/Hair_2", path);
            Assert.AreSame(_hair2, AvatarRoots.Find(root, path));
        }

        [Test]
        public void RootItself_IsEmptyPath()
        {
            Transform root = _avatar.transform;
            Assert.AreEqual("", AvatarRoots.PathFrom(root, root));
            Assert.AreSame(root, AvatarRoots.Find(root, ""));
        }

        [Test]
        public void OutsideTheRoot_HasNoPath()
        {
            var other = new GameObject("Other");
            try
            {
                Assert.IsNull(AvatarRoots.PathFrom(_avatar.transform, other.transform));
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void RenamedBone_IsNotFound()
        {
            Transform root = _avatar.transform;
            string path = AvatarRoots.PathFrom(root, _hair2);
            _hair1.name = "Renamed";
            Assert.IsNull(AvatarRoots.Find(root, path));
        }

        [Test]
        public void WithoutSdk_TopAnimatorIsTheRoot()
        {
            // SDK の有無で答えが変わるので、SDK が無い環境でだけ確かめる。
            if (AvatarRoots.HasVrcSdk) Assert.Ignore("VRChat SDK があるので Animator での判定は使われない");

            // 途中にも Animator があっても、一番上のものをアバターとみなす。
            _hips.gameObject.AddComponent<Animator>();
            Assert.AreSame(_avatar.transform, AvatarRoots.FindRoot(_hair2));
        }
    }
}
