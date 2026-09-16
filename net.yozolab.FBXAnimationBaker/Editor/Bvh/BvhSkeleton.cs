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
    /// BVH は右手系。Unity は左手系・Y アップ。基底を移すのに X 軸を反転させる。
    ///   Y アップ  位置 (x, y, z) -> (-x,  y,  z)
    ///   Z アップ  位置 (x, y, z) -> (-x,  z, -y)
    /// 反転は基底の取り替えであって、形を鏡像にするわけではない。位置と回転の両方へ
    /// 一貫して当てているので、骨格の見た目は変わらない。
    ///
    /// Z アップぶんは、Y アップの変換に「X 軸まわり -90 度」を合成したもの。
    /// 回転は合成した基底変換 M で軸を移し、行列式が負なので角度の符号を返す。
    /// 結果として軸ごとの対応はこうなる。
    ///   Y アップ  X:+X   Y:-Y   Z:-Z
    ///   Z アップ  X:+X   Y:+Z   Z:-Y
    /// </summary>
    public sealed class BvhSkeleton
    {
        public GameObject Root { get; private set; }

        /// <summary>BVH のジョイント -> 生成した Transform。</summary>
        public IReadOnlyDictionary<BvhJoint, Transform> Bones => bones;

        public BvhFile File { get; }

        /// <summary>
        /// 単位換算。BVH の OFFSET は cm のことも m のこともある。
        ///
        /// もっとも、リターゲットは Humanoid の正規化を通るので、骨格全体を
        /// 一様に拡大縮小しても結果は変わらない。ここが効くのは、値が極端すぎて
        /// Avatar を組めない場合だけ。
        /// </summary>
        public float Scale { get; }

        /// <summary>解決済みの上方向（Auto は解決してから渡ってくる）。</summary>
        public BvhUpAxis UpAxis { get; }

        private readonly Dictionary<BvhJoint, Transform> bones = new Dictionary<BvhJoint, Transform>();

        /// <summary>rest pose で腰を置いた高さ。<see cref="ApplyRestPose"/> で戻すのに要る。</summary>
        private float restRootHeight;

        private BvhSkeleton(BvhFile file, float scale, BvhUpAxis upAxis)
        {
            File = file;
            Scale = scale;
            UpAxis = upAxis == BvhUpAxis.Auto ? file.GuessUpAxis() : upAxis;
        }

        /// <summary>
        /// 休めのポーズ（OFFSET だけ、回転なし）で階層を作る。
        /// BVH の rest pose は普通 T ポーズなので、そのまま Humanoid Avatar の元にできる。
        /// </summary>
        public static BvhSkeleton Build(BvhFile file, float scale, string rootName,
                                        BvhUpAxis upAxis = BvhUpAxis.Auto)
        {
            var skeleton = new BvhSkeleton(file, scale, upAxis);

            skeleton.Root = new GameObject(string.IsNullOrEmpty(rootName) ? file.Root.Name : rootName);
            skeleton.CreateBone(file.Root, skeleton.Root.transform);
            skeleton.StandRestPose();

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

        /// <summary>
        /// rest pose の腰を、立っている高さまで持ち上げる。
        ///
        /// BVH の ROOT は OFFSET が 0 のことが多く、腰の高さは MOTION 側が持っている。
        /// そのまま rest pose にすると「腰が原点・足が地面より下」の骨格になる。
        /// Unity は Avatar の humanScale を腰の高さから決めるため、この形だと
        /// 極端に小さい値になり(実測で 0.035、まともなアバターは 1 前後)、
        /// 正規化されたポーズが桁違いに大きくなって、リターゲット先の腰が跳ね上がる。
        ///
        /// 高さは最初のフレームの腰の位置から採る。その BVH 自身が「立っている」と
        /// している高さなので、幾何的に足の裏を推測するより素直で、末端の End Site を
        /// 親の OFFSET で埋めているようなファイルにも引きずられない。
        /// </summary>
        private void StandRestPose()
        {
            if (File.Frames.Count == 0) return;
            if (!bones.TryGetValue(File.Root, out Transform rootBone)) return;

            Vector3 firstFramePosition = ReadRootPosition(0);
            if (Mathf.Approximately(firstFramePosition.y, 0f)) return;

            rootBone.localPosition = new Vector3(
                rootBone.localPosition.x, firstFramePosition.y, rootBone.localPosition.z);

            restRootHeight = firstFramePosition.y;
        }

        /// <summary>指定フレームでの ROOT ジョイントの位置(Unity 座標)。</summary>
        private Vector3 ReadRootPosition(int frameIndex)
        {
            BvhJoint root = File.Root;
            float[] row = File.Frames[frameIndex];
            Vector3 translation = root.Offset;

            for (int i = 0; i < root.Channels.Count; i++)
            {
                float value = row[root.ChannelStart + i];
                switch (root.Channels[i])
                {
                    case BvhChannel.PositionX: translation.x = value; break;
                    case BvhChannel.PositionY: translation.y = value; break;
                    case BvhChannel.PositionZ: translation.z = value; break;
                }
            }

            return ConvertPosition(translation);
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
                        case BvhChannel.RotationX:
                            rotation *= Quaternion.AngleAxis(value, Vector3.right);
                            break;
                        case BvhChannel.RotationY:
                            rotation *= UpAxis == BvhUpAxis.Z
                                ? Quaternion.AngleAxis(value, Vector3.forward)
                                : Quaternion.AngleAxis(-value, Vector3.up);
                            break;
                        case BvhChannel.RotationZ:
                            rotation *= UpAxis == BvhUpAxis.Z
                                ? Quaternion.AngleAxis(-value, Vector3.up)
                                : Quaternion.AngleAxis(-value, Vector3.forward);
                            break;
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
                Vector3 position = ConvertPosition(pair.Key.Offset);
                if (pair.Key == File.Root) position.y = restRootHeight;

                pair.Value.localPosition = position;
                pair.Value.localRotation = Quaternion.identity;
            }
        }

        private Vector3 ConvertPosition(Vector3 bvhPosition)
        {
            Vector3 converted = UpAxis == BvhUpAxis.Z
                ? new Vector3(-bvhPosition.x, bvhPosition.z, -bvhPosition.y)
                : new Vector3(-bvhPosition.x, bvhPosition.y, bvhPosition.z);

            return converted * Scale;
        }

    }
}
