using System.Collections.Generic;
using UnityEngine;

namespace YozoLab.FBXAnimationBaker.Bvh
{
    /// <summary>
    /// BVH のジョイント階層を Unity の Transform として組み、フレームごとに姿勢を当てる。
    ///
    /// アニメーションクリップは作らない。リターゲットは <see cref="UnityEngine.HumanPoseHandler"/>
    /// でポーズを直接吸い出す形にしたので、この骨格は「フレーム n の姿勢に出来る入れ物」
    /// でありさえすればよく、カーブを経由する必要が無い。
    ///
    /// ── 座標系 ────────────────────────────────────────────────────
    /// BVH は右手系・Y アップ。Unity は左手系・Y アップ。X 軸を反転させて基底を移す。
    ///   位置    (x, y, z) -> (-x, y, z)
    ///   回転    q=(x,y,z,w) -> (x, -y, -z, w)
    /// 反転は基底の取り替えであって、形を鏡像にするわけではない。位置と回転の両方へ
    /// 一貫して当てているので、骨格の見た目は変わらない。
    /// </summary>
    public sealed class BvhSkeleton
    {
        public GameObject Root { get; private set; }

        /// <summary>BVH のジョイント -> 生成した Transform。</summary>
        public IReadOnlyDictionary<BvhJoint, Transform> Bones => bones;

        public BvhFile File { get; }

        /// <summary>単位換算。BVH の OFFSET は多くが cm なので既定は 0.01。</summary>
        public float Scale { get; }

        private readonly Dictionary<BvhJoint, Transform> bones = new Dictionary<BvhJoint, Transform>();

        private BvhSkeleton(BvhFile file, float scale)
        {
            File = file;
            Scale = scale;
        }

        /// <summary>
        /// 休めのポーズ（OFFSET だけ、回転なし）で階層を作る。
        /// BVH の rest pose は普通 T ポーズなので、そのまま Humanoid Avatar の元にできる。
        /// </summary>
        public static BvhSkeleton Build(BvhFile file, float scale, string rootName)
        {
            var skeleton = new BvhSkeleton(file, scale);

            skeleton.Root = new GameObject(string.IsNullOrEmpty(rootName) ? file.Root.Name : rootName);
            skeleton.CreateBone(file.Root, skeleton.Root.transform);

            return skeleton;
        }

        /// <summary>
        /// BVH の ROOT も含め、全ジョイントを子として作る。
        ///
        /// ROOT を生成済みの親オブジェクトそのものに割り当てないのは、Humanoid の
        /// Avatar が Hips の上にもう 1 段あることを前提にしているため。
        /// </summary>
        private void CreateBone(BvhJoint joint, Transform parent)
        {
            Transform bone = new GameObject(joint.Name).transform;
            bone.SetParent(parent, false);

            bone.localPosition = ConvertPosition(joint.Offset);
            bone.localRotation = Quaternion.identity;
            bone.localScale = Vector3.one;

            bones[joint] = bone;

            foreach (BvhJoint child in joint.Children)
            {
                CreateBone(child, bone);
            }
        }

        /// <summary>指定フレームの姿勢を Transform へ当てる。</summary>
        public void ApplyFrame(int frameIndex)
        {
            if (frameIndex < 0 || frameIndex >= File.Frames.Count) return;

            float[] row = File.Frames[frameIndex];

            foreach (BvhJoint joint in File.Joints)
            {
                if (!bones.TryGetValue(joint, out Transform bone)) continue;
                if (joint.Channels.Count == 0) continue; // End Site

                Vector3 translation = joint.Offset;
                Quaternion rotation = Quaternion.identity;
                bool hasTranslation = false;

                for (int i = 0; i < joint.Channels.Count; i++)
                {
                    float value = row[joint.ChannelStart + i];

                    switch (joint.Channels[i])
                    {
                        case BvhChannel.PositionX: translation.x = value; hasTranslation = true; break;
                        case BvhChannel.PositionY: translation.y = value; hasTranslation = true; break;
                        case BvhChannel.PositionZ: translation.z = value; hasTranslation = true; break;

                        // 宣言順がそのまま適用順。"Zrotation Xrotation Yrotation" なら
                        // R = Rz * Rx * Ry になるので、宣言順に右から掛けていけばよい。
                        case BvhChannel.RotationX: rotation *= Quaternion.AngleAxis(value, Vector3.right); break;
                        case BvhChannel.RotationY: rotation *= Quaternion.AngleAxis(-value, Vector3.up); break;
                        case BvhChannel.RotationZ: rotation *= Quaternion.AngleAxis(-value, Vector3.forward); break;
                    }
                }

                bone.localPosition = ConvertPosition(hasTranslation ? translation : joint.Offset);
                bone.localRotation = rotation;
            }
        }

        /// <summary>休めのポーズ（OFFSET だけ）へ戻す。</summary>
        public void ApplyRestPose()
        {
            foreach (KeyValuePair<BvhJoint, Transform> pair in bones)
            {
                pair.Value.localPosition = ConvertPosition(pair.Key.Offset);
                pair.Value.localRotation = Quaternion.identity;
            }
        }

        private Vector3 ConvertPosition(Vector3 bvhPosition)
        {
            return new Vector3(-bvhPosition.x, bvhPosition.y, bvhPosition.z) * Scale;
        }

    }
}
