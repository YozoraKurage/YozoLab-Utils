using System;
using UnityEngine;
using UnityEngine.Animations;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Utils;

namespace YozoLab.SPS.Service {
    /** This builder is responsible for creating a fake head bone, and moving
     * objects onto it, if those objects should be visible in first person.
     */
    [VFService]
    /**
     * 一人称視点で見せたい物を登録する VRCHeadChop の置き場所。
     * （VRCFury ではコンストレイントで作る「常に見える頭」も担っていたが、SPS では使わない。）
     */
    internal class FakeHeadService {
        [VFAutowired] private readonly VRCFArmatureCache armatureCache;

        private readonly Lazy<VFGameObject> headChopObj;
        public FakeHeadService() {
            headChopObj = new Lazy<VFGameObject>(() => {
                var head = armatureCache.FindBoneOnArmatureOrException(HumanBodyBones.Head);
                return GameObjects.Create("SpsHeadChop", head);
            });
        }

        public VFGameObject GetHeadChopObj() {
            return headChopObj.Value;
        }
    }
}
