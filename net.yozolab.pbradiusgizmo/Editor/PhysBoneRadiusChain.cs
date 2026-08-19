// VRChat SDK（com.vrchat.base）がある環境でだけコンパイルされる。
// 判定は asmdef の versionDefines に任せてあるので、手動設定は要らない。
#if YOZOLAB_PBRADIUS_VRCSDK
using System.Collections.Generic;
using UnityEngine;
using VRC.Dynamics;

namespace YozoLab.PBRadiusGizmo
{
    /// <summary>
    /// PhysBone の Collision Radius が実際にどこへどの大きさで出るかを組み立てる。
    ///
    /// 「SDK のギズモとぴったり重なるオーバーレイ」でないと、掴んだ球と
    /// 当たり判定の球がずれて見え、目分量で合わせる道具として意味を失う。
    /// そのため形の決め方を SDK のギズモに揃えてある。
    ///   ・半径は bone→child の 2 点を結ぶ「先細りカプセル」。両端の半径は
    ///     CalcRadius(その位置の比率) × そのボーンのスケール。
    ///   ・手前側の半径が 0 なら、子側に球を 1 つだけ描く。
    ///   ・子の決め方は childCount と multiChildType 次第。末端は endpointPosition。
    ///   ・スケールは lossyScale の x/y/z の最大値（PhysBoneManager.CalcBoneScale）。
    /// </summary>
    internal static class PhysBoneRadiusChain
    {
        /// <summary>描く図形 1 つ分。世界座標で持つ。</summary>
        internal struct Segment
        {
            public Vector3 start;        // ボーン側の位置
            public Vector3 end;          // 子（または末端）側の位置
            public Quaternion rotation;  // up がボーン軸を向く回転。SDK のカプセルと同じ
            public float startRadius;    // ボーン側の半径（世界座標）
            public float endRadius;      // 子側の半径（世界座標）
            public bool isSphere;        // true なら end に球を 1 つ描くだけ

            /// <summary>ハンドルを置く点。球なら子側、カプセルならボーン側。</summary>
            public Vector3 HandleCenter => isSphere ? end : start;

            /// <summary>ハンドルを置く点での半径（世界座標）。</summary>
            public float HandleRadius => isSphere ? endRadius : startRadius;

            /// <summary>
            /// その点での「Radius 1 あたりの世界座標での半径」。
            /// カーブの値とボーンのスケールを掛けたもので、書き戻しの逆算に使う。
            /// </summary>
            public float handleFactor;
        }

        /// <summary>
        /// <paramref name="pb"/> の半径ギズモを組み立てて <paramref name="into"/> へ詰める。
        ///
        /// ボーン構造は PhysBoneInitGate 経由で「変更があったときだけ」作り直す。
        /// 頂点は毎回 transform から読むので、ボーンが動くだけなら作り直しは要らない。
        /// </summary>
        internal static void Build(VRCPhysBoneBase pb, List<Segment> into)
        {
            into.Clear();
            if (pb == null) return;

            PhysBoneInitGate.EnsureInitialized(pb);

            List<VRCPhysBoneBase.Bone> bones = pb.bones;
            if (bones == null) return;

            float radius = pb.radius;

            for (int i = 0; i < bones.Count; i++)
            {
                VRCPhysBoneBase.Bone bone = bones[i];
                if (bone.transform == null) continue;

                Vector3 position = bone.transform.position;
                float ratio = pb.CalcTransformRatio(bone.boneChainIndex);
                float boneScale = PhysBoneManager.CalcBoneScale(bone.transform.lossyScale);
                float childScale = boneScale;

                // 子（またはそれに相当する点）を決める。SDK と同じ順序で判定する。
                Vector3 childLocalPosition;
                Vector3 childPosition;

                bool hasSingleChild = bone.childCount == 1
                                      || (bone.childCount > 1
                                          && pb.multiChildType == VRCPhysBoneBase.MultiChildType.First);

                if (hasSingleChild)
                {
                    if (bone.childIndex < 0 || bone.childIndex >= bones.Count) continue;
                    VRCPhysBoneBase.Bone child = bones[bone.childIndex];
                    if (child.transform == null) continue;

                    childLocalPosition = child.transform.localPosition;
                    childPosition = child.transform.position;
                    childScale = PhysBoneManager.CalcBoneScale(child.transform.lossyScale);
                }
                else if (bone.isEndBone)
                {
                    // 末端に伸ばす分が無ければ、そのボーンには何も出ない。
                    if (pb.endpointPosition == Vector3.zero) continue;

                    childLocalPosition = pb.endpointPosition;
                    childPosition = bone.transform.TransformPoint(childLocalPosition);
                }
                else if (pb.multiChildType == VRCPhysBoneBase.MultiChildType.Average)
                {
                    childLocalPosition = bone.averageChildPos;
                    childPosition = bone.transform.TransformPoint(childLocalPosition);
                }
                else
                {
                    // Ignore：枝分かれの手前で打ち切られる。
                    continue;
                }

                if (radius <= 0f) continue;

                float childRatio = pb.CalcTransformRatio(bone.boneChainIndex + 1);
                float startRadius = pb.CalcRadius(ratio) * boneScale;
                float endRadius = pb.CalcRadius(childRatio) * childScale;
                if (endRadius <= 0f) continue;

                Vector3 direction = childLocalPosition.normalized;
                if (direction == Vector3.zero) continue;

                Quaternion rotation = bone.transform.rotation
                                      * Quaternion.FromToRotation(Vector3.up, direction);

                if (startRadius > 0f)
                {
                    into.Add(new Segment
                    {
                        start = position,
                        end = childPosition,
                        rotation = rotation,
                        startRadius = startRadius,
                        endRadius = endRadius,
                        isSphere = false,
                        handleFactor = startRadius / radius,
                    });
                }
                else
                {
                    into.Add(new Segment
                    {
                        start = position,
                        end = childPosition,
                        rotation = rotation,
                        startRadius = 0f,
                        endRadius = endRadius,
                        isSphere = true,
                        handleFactor = endRadius / radius,
                    });
                }
            }
        }

        /// <summary>
        /// Radius が 0 のときにハンドルを置く場所と倍率。
        ///
        /// 0 だと図形が 1 つも出ないので <see cref="Build"/> の結果からは決められない。
        /// かといってハンドルを消すと 0 から増やす手立てが無くなるため、
        /// 先頭のボーンを基準に「Radius を 1 にしたときの世界座標での半径」を測る。
        /// 一時的に radius を書き換えるが、シリアライズには触れないので
        /// アセットにもプレハブにも痕跡は残らない。
        /// </summary>
        internal static bool TryGetZeroRadiusAnchor(
            VRCPhysBoneBase pb, out Vector3 center, out Quaternion rotation, out float factor)
        {
            center = Vector3.zero;
            rotation = Quaternion.identity;
            factor = 0f;

            if (pb == null) return false;

            PhysBoneInitGate.EnsureInitialized(pb);
            List<VRCPhysBoneBase.Bone> bones = pb.bones;
            if (bones == null || bones.Count == 0) return false;

            float saved = pb.radius;
            try
            {
                pb.radius = 1f;
                for (int i = 0; i < bones.Count; i++)
                {
                    VRCPhysBoneBase.Bone bone = bones[i];
                    if (bone.transform == null) continue;

                    float ratio = pb.CalcTransformRatio(bone.boneChainIndex);
                    float scale = PhysBoneManager.CalcBoneScale(bone.transform.lossyScale);
                    float candidate = pb.CalcRadius(ratio) * scale;
                    if (candidate <= 0f) continue;

                    center = bone.transform.position;
                    rotation = bone.transform.rotation;
                    factor = candidate;
                    return true;
                }
            }
            finally
            {
                pb.radius = saved;
            }

            return false;
        }
    }
}
#endif
