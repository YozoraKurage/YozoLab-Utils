using JetBrains.Annotations;
using UnityEngine;
using YozoLab.SPS.Component;
using YozoLab.SPS.Utils;

namespace YozoLab.SPS.Builder.Haptics {
    internal interface SpsAutoTagGenerator {
        HumanBodyBones? GetClosestBone(VFGameObject obj);
        [CanBeNull] VFGameObject GetBone(VFGameObject obj, HumanBodyBones bone);
        Vector3? GetAvatarViewPosition(VFGameObject obj);
    }
}
