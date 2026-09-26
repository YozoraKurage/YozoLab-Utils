using YozoLab.SPS.Inspector;
using System;
using nadena.dev.modular_avatar.core;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Builder.Haptics;
using YozoLab.SPS.Hooks;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Model;
using YozoLab.SPS.Model.Feature;
using YozoLab.SPS.Service;
using YozoLab.SPS.Utils;
using VRC.SDK3.Avatars.Components;

namespace YozoLab.SPS.Utils {
    [VFService]
    internal class ClosestBoneUtils {
        private static readonly Dictionary<VFGameObject, ClosestBoneUtils> perFrame
            = new Dictionary<VFGameObject, ClosestBoneUtils>();

        private readonly VRCFObjectPathCache objectPaths;
        private readonly VRCFArmatureCache armatureCache;
        private readonly Dictionary<VFGameObject, HumanBodyBones?> results = new();
        private readonly Dictionary<VFGameObject, Dictionary<VFGameObject, VFGameObject>> maParents = new();

        [VFAutowired]
        public ClosestBoneUtils(VRCFObjectPathCache objectPaths, VRCFArmatureCache armatureCache) {
            this.objectPaths = objectPaths;
            this.armatureCache = armatureCache;
        }

        public static ClosestBoneUtils GetPerFrame(VFGameObject avatarObject) {
            return perFrame.GetOrCreate(
                avatarObject,
                () => new ClosestBoneUtils(
                    VRCFObjectPathCache.GetPerFrame(avatarObject),
                    VRCFArmatureCache.GetPerFrame(avatarObject)
                )
            );
        }

        [VFInit]
        private static void Init() {
            Scheduler.Schedule(perFrame.Clear, 0);
        }

        /**
         * Modular Avatar が後で付け替える先。SPS は MA がボーンを統合する前に動くので、
         * 衣装などのボーンが最終的にどのボーンへ付くかを、MA のコンポーネントから引いておく。
         * （VRCFury では Armature Link を辿っていた部分。）
         * - Merge Armature: MA 自身の対応表（GetBonesMapping）
         * - Bone Proxy: target
         */
        private Dictionary<VFGameObject, VFGameObject> GetMaParents(VFGameObject rootObject) {
            if (maParents.TryGetValue(rootObject, out var cached)) return cached;
            var output = new Dictionary<VFGameObject, VFGameObject>();
            foreach (var merge in rootObject.GetComponentsInSelfAndChildren<ModularAvatarMergeArmature>()) {
                try {
                    foreach (var (from, to) in merge.GetBonesMapping()) {
                        if (from != null && to != null) output[from] = to;
                    }
                } catch (Exception e) {
                    Debug.LogWarning($"[YozoLab SPS] Merge Armature の対応を読めませんでした: {merge.name}\n{e}");
                }
            }
            foreach (var proxy in rootObject.GetComponentsInSelfAndChildren<ModularAvatarBoneProxy>()) {
                var target = proxy.target;
                if (target != null) output[proxy.transform] = target;
            }
            return maParents[rootObject] = output;
        }

        public HumanBodyBones? GetClosestHumanoidBone(VFGameObject obj) {
            return results.GetOrCreate(obj, () => GetClosestHumanoidBoneUncached(obj));
        }

        [CanBeNull]
        public VFGameObject GetBone(VFGameObject obj, HumanBodyBones bone) {
            return armatureCache.FindBoneOnArmatureOrNull(bone);
        }

        private HumanBodyBones? GetClosestHumanoidBoneUncached(VFGameObject obj) {
            var avatarObject = obj.GetAvatarRoot();

            var followConstraints = true;
            var followModularAvatar = true;

            var maParentMap = GetMaParents(avatarObject);

            var humanoidBones = armatureCache.GetAllBones()
                .ToDictionary(x => x.Value, x => x.Key);
            var alreadyChecked = new HashSet<VFGameObject>();
            var current = obj;
            while (current != null) {
                if (humanoidBones.TryGetValue(current, out var bone))
                    return bone;

                alreadyChecked.Add(current);

                if (followModularAvatar) {
                    if (maParentMap.TryGetValue(current, out var foundParent)
                        && foundParent != null
                        && !alreadyChecked.Contains(foundParent)) {
                        current = foundParent;
                        continue;
                    }
                }
                
                if (followConstraints) {
                    var positionTo = current.GetConstraints()
                        .Where(c => c.IsParent() || c.IsPosition())
                        .Select(c => c.GetFirstSource())
                        .NotNull()
                        .FirstOrDefault();
                    if (positionTo != null && !alreadyChecked.Contains(positionTo)) {
                        current = positionTo;
                        continue;
                    }
                }
                current = current.parent;
            }
            return null;
        }
    }

    [VFService]
    internal class SpsAvatarAutoTagGenerator : SpsAutoTagGenerator {
        [VFAutowired] private readonly ClosestBoneUtils closestBoneUtils;
        [VFAutowired] [CanBeNull] private readonly VRCAvatarDescriptor avatar;

        public HumanBodyBones? GetClosestBone(VFGameObject obj) {
            return closestBoneUtils.GetClosestHumanoidBone(obj);
        }

        [CanBeNull]
        public VFGameObject GetBone(VFGameObject obj, HumanBodyBones bone) {
            return closestBoneUtils.GetBone(obj, bone);
        }

        public Vector3? GetAvatarViewPosition(VFGameObject obj) {
            return (avatar ?? obj.GetAvatarRoot().GetComponent<VRCAvatarDescriptor>())?.ViewPosition;
        }
    }
}
