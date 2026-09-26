using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Feature.Base;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Inspector;
using YozoLab.SPS.Model.Feature;
using YozoLab.SPS.Service;
using YozoLab.SPS.Utils;
using VRC.SDK3.Avatars.Components;

namespace YozoLab.SPS.Feature {
    [FeatureTitle("Show In First Person")]
    /**
     * 頭の子にある SPS の Socket / Plug を、一人称視点でも見えるようにする。
     *
     * VRCFury では Armature Link で頭の代わりのボーンへ付け替える経路も持っていたが、
     * VRCHeadChop がある今の VRCSDK では、SPS からの呼び出し（頭の子に限る）は
     * VRCHeadChop に登録するだけで済んでいた。ここではその経路だけを残す。
     */
    internal class ShowInFirstPersonBuilder : FeatureBuilder<ShowInFirstPerson> {
        [VFAutowired] private readonly FakeHeadService fakeHead;
        [VFAutowired] private readonly VRCFArmatureCache armatureCache;

        [FeatureBuilderAction]
        public void Apply() {
            var obj = model.useObjOverride ? model.objOverride.asVf() : featureBaseObject;
            if (obj == null) return;

            var head = armatureCache.FindBoneOnArmatureOrNull(HumanBodyBones.Head);
            if (head == null) return;
            if (!obj.IsSameOrChildOf(head)) return;

            var headChopObj = fakeHead.GetHeadChopObj();
            var headChop = headChopObj.GetComponents<VRCHeadChop>().FirstOrDefault(c => c.targetBones.Length < 32);
            if (headChop == null) headChop = headChopObj.AddComponent<VRCHeadChop>();
            headChop.targetBones = headChop.targetBones.Append(new VRCHeadChop.HeadChopBone() {
                transform = obj,
                scaleFactor = 1,
                applyCondition = VRCHeadChop.HeadChopBone.ApplyCondition.AlwaysApply
            }).ToArray();
        }

        [FeatureEditor]
        public static VisualElement Editor() {
            return VRCFuryEditorUtils.Info(
                "This component will automatically make this GameObject a child of the head bone, and will" +
                " use constraint tricks to make it visible in first person.\n\n" +
                "Warning:\n" +
                "* Do not combine this with armature link.\n" +
                "* First person objects cannot be attached to non-first-person" +
                " objects, such as nose or ear bones.\n" +
                "* World position will be maintained when reparented.");
        }
    }
}
