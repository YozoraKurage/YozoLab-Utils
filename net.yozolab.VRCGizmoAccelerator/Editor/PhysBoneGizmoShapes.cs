// VRChat SDK（com.vrchat.base）がある環境でだけコンパイルされる。
#if YOZOLAB_VRCGIZMOACC_VRCSDK
using System.Collections.Generic;
using UnityEngine;
using VRC.Dynamics;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// PhysBone の既定形状（ボーン線と Collision Radius）を組み立てる。
    ///
    /// SDK のギズモと同じ位置・同じ大きさになるよう、形の決め方を揃えてある。
    ///   ・ボーンごとに bone→child の線を 1 本。
    ///   ・radius > 0 なら先細りカプセル。手前側の半径が 0 なら子側に球 1 つ。
    ///   ・半径は CalcRadius(CalcTransformRatio(index)) × ボーンのスケール
    ///     （スケールは lossyScale の最大成分 = PhysBoneManager.CalcBoneScale）。
    ///   ・子の決め方は childCount と multiChildType。末端は endpointPosition。
    ///   ・色は白、アルファは (選択中 ? 1 : 0.5) × boneOpacity × 0.5。
    ///   ・角度制限（limitType）は Angle=コーン / Hinge=扇 / Polar=矩形枠 を
    ///     _LimitColor(0.2, 1, 1) × limitOpacity で描く（塗りはさらに減衰）。
    /// </summary>
    internal static class PhysBoneGizmoShapes
    {
        // SDK の VRCPhysBoneEditor._LimitColor と同じ
        private static readonly Color LimitColor = new Color(0.2f, 1f, 1f);

        internal static void Build(VRCPhysBoneBase pb, PhysBoneGizmoCanvas canvas)
        {
            // 構成が変わったときだけ作り直す（PhysBoneInitGate）。頂点は毎回
            // transform から読むので、ボーンが動くだけなら作り直しは要らない。
            PhysBoneInitGate.EnsureInitialized(pb);

            List<VRCPhysBoneBase.Bone> bones = pb.bones;
            if (bones == null) return;

            float alphaFactor = canvas.Selected ? 1f : 0.5f;
            float boneAlpha = alphaFactor * pb.boneOpacity * 0.5f;
            var color = new Color(1f, 1f, 1f, boneAlpha);

            bool drawLimits = pb.limitType != VRCPhysBoneBase.LimitType.None && pb.limitOpacity > 0f;
            if (boneAlpha <= 0f && !drawLimits) return;

            float radius = pb.radius;

            for (int i = 0; i < bones.Count; i++)
            {
                VRCPhysBoneBase.Bone bone = bones[i];
                if (bone.transform == null) continue;

                Vector3 position = bone.transform.position;
                float ratio = pb.CalcTransformRatio(bone.boneChainIndex);
                float boneScale = PhysBoneManager.CalcBoneScale(bone.transform.lossyScale);
                float childScale = boneScale;

                // 子（またはそれに相当する点）。SDK と同じ順序で判定する。
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
                    // Ignore：枝分かれの手前で打ち切り。
                    continue;
                }

                if (boneAlpha > 0f)
                {
                    canvas.AddLine(position, childPosition, color);

                    if (radius > 0f)
                    {
                        float childRatio = pb.CalcTransformRatio(bone.boneChainIndex + 1);
                        float startRadius = pb.CalcRadius(ratio) * boneScale;
                        float endRadius = pb.CalcRadius(childRatio) * childScale;

                        Vector3 direction = childLocalPosition.normalized;

                        if (endRadius > 0f && direction != Vector3.zero)
                        {
                            Quaternion rotation = bone.transform.rotation
                                                  * Quaternion.FromToRotation(Vector3.up, direction);

                            if (startRadius > 0f)
                            {
                                canvas.AddTaperedCapsule(
                                    position, childPosition, rotation, startRadius, endRadius, color);
                            }
                            else
                            {
                                canvas.AddWireSphere(childPosition, rotation, endRadius, color);
                            }
                        }
                    }
                }

                if (drawLimits)
                {
                    float boneRatio = pb.CalcBoneRatio(bone.boneChainIndex);
                    BuildLimits(pb, canvas, bone, position, childPosition,
                        childLocalPosition, boneRatio, alphaFactor);
                }
            }
        }

        // ---------------------------------------------------------------
        // 角度制限（limitType）。SDK の表示と同じ形になるよう合わせる
        // ---------------------------------------------------------------

        /// <summary>
        /// 制限ギズモ 1 ボーン分。回転は restRotation（レスト姿勢）基準で、
        /// 親があれば親のワールド回転を掛ける（SDK と同じ）。
        /// 再生中に SDK はチェーンの元回転を引くが、ここでは restRotation で代用する。
        /// </summary>
        private static void BuildLimits(
            VRCPhysBoneBase pb, PhysBoneGizmoCanvas canvas, VRCPhysBoneBase.Bone bone,
            Vector3 position, Vector3 childPosition, Vector3 childLocalPosition,
            float boneRatio, float alphaFactor)
        {
            Vector2 maxAngle = pb.CalcMaxAngle(boneRatio);
            float distance = Vector3.Distance(position, childPosition);
            Vector3 limitRotation = pb.CalcLimitRotation(boneRatio);
            PhysBoneManager.CalcLimitAxis(childLocalPosition, limitRotation,
                out Vector3 axisA, out Vector3 axisB);

            float limitAlpha = pb.limitOpacity * alphaFactor;
            Color full = LimitColor;
            full.a = limitAlpha;

            Quaternion restRotation = bone.restRotation;
            Transform parent = bone.transform.parent;

            switch (pb.limitType)
            {
                case VRCPhysBoneBase.LimitType.Angle:
                {
                    float size = distance * 0.5f;
                    Quaternion rotation = restRotation
                                          * Quaternion.FromToRotation(Vector3.up, axisB);
                    if (parent != null) rotation = parent.rotation * rotation;

                    Color fill = full;
                    fill.a = limitAlpha * 0.125f;

                    AddWireAngleCone(canvas, position, rotation, size, maxAngle.x, 4, full);
                    AddSolidAngleCone(canvas, position, rotation, size, maxAngle.x, fill);
                    break;
                }

                case VRCPhysBoneBase.LimitType.Hinge:
                {
                    float size = distance * 0.4f;
                    Quaternion rotation = restRotation;
                    if (parent != null) rotation = parent.rotation * rotation;

                    Color fill = full;
                    fill.a = limitAlpha * 0.25f;

                    canvas.AddWireDisc(position, rotation * axisA, size, full);
                    canvas.AddSolidArc(position, rotation * axisA, rotation * axisB,
                        maxAngle.x, size, fill);
                    canvas.AddSolidArc(position, rotation * axisA, rotation * axisB,
                        -maxAngle.x, size, fill);
                    break;
                }

                case VRCPhysBoneBase.LimitType.Polar:
                {
                    float size = distance * 0.4f;
                    Quaternion rotation = restRotation;
                    if (parent != null) rotation = parent.rotation * rotation;

                    Vector3 cross = Vector3.Cross(axisA, axisB);

                    Color fill = full;
                    fill.a = limitAlpha * 0.25f;

                    canvas.AddWireDisc(position, rotation * axisA, size, full);

                    // 扇（x 方向の可動域）
                    canvas.AddSolidArc(position, rotation * axisA,
                        rotation * (Quaternion.AngleAxis(-maxAngle.x, axisA) * axisB),
                        maxAngle.x * 2f, size, fill);

                    // 枠の上下辺（y 方向に傾けた弧）
                    canvas.AddWireArc(position, rotation * axisA,
                        rotation * (Quaternion.AngleAxis(-maxAngle.x, axisA)
                                    * (Quaternion.AngleAxis(90f - maxAngle.y, cross) * axisA)),
                        maxAngle.x * 2f, size, full);
                    canvas.AddWireArc(position, rotation * axisA,
                        rotation * (Quaternion.AngleAxis(-maxAngle.x, axisA)
                                    * (Quaternion.AngleAxis(90f - maxAngle.y, -cross) * -axisA)),
                        maxAngle.x * 2f, size, full);

                    // 枠の左右辺（±x の端で y 方向へ振った弧）
                    Quaternion edgePlus = rotation * Quaternion.AngleAxis(maxAngle.x, axisA);
                    canvas.AddWireArc(position, edgePlus * cross,
                        edgePlus * (Quaternion.AngleAxis(-maxAngle.y, cross) * axisB),
                        maxAngle.y * 2f, size, full);

                    Quaternion edgeMinus = rotation * Quaternion.AngleAxis(-maxAngle.x, axisA);
                    canvas.AddWireArc(position, edgeMinus * cross,
                        edgeMinus * (Quaternion.AngleAxis(-maxAngle.y, cross) * axisB),
                        maxAngle.y * 2f, size, full);
                    break;
                }
            }
        }

        /// <summary>HandlesUtil.DrawWireAngleCone の移植。放射線 + 底面の輪。</summary>
        private static void AddWireAngleCone(
            PhysBoneGizmoCanvas canvas, Vector3 tip, Quaternion rotation,
            float radius, float angle, int segments, Color color)
        {
            Vector3 rim = Quaternion.AngleAxis(angle, Vector3.forward)
                          * new Vector3(0f, radius, 0f);

            for (int i = 0; i < segments; i++)
            {
                Vector3 direction = Quaternion.AngleAxis(360f * i / segments, Vector3.up) * rim;
                canvas.AddLine(tip, tip + rotation * direction, color);
            }

            canvas.AddWireDisc(
                tip + rotation * new Vector3(0f, rim.y, 0f),
                rotation * Vector3.up,
                Mathf.Abs(rim.x),
                color);
        }

        /// <summary>
        /// HandlesUtil.DrawSolidAngleCone の移植。normal と直交しない from を
        /// 360° 掃引することで、円錐の側面が塗りで出る。
        /// </summary>
        private static void AddSolidAngleCone(
            PhysBoneGizmoCanvas canvas, Vector3 tip, Quaternion rotation,
            float radius, float angle, Color color)
        {
            canvas.AddSolidArc(
                tip,
                rotation * Vector3.up,
                rotation * Quaternion.AngleAxis(angle, Vector3.forward) * Vector3.up,
                360f, radius, color);
        }
    }
}
#endif
