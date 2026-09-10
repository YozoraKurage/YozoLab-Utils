using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace YozoLab.FBXAnimationBaker.Bvh
{
    /// <summary>
    /// BVH の骨格へ Humanoid Avatar を組む。
    ///
    /// リターゲットを自前でやらずに済ませるための土台。BVH 側にも Humanoid Avatar が
    /// あれば、あとは <see cref="UnityEngine.HumanPoseHandler"/> で「筋肉空間のポーズ」を
    /// 吸い出して相手へ流すだけになり、スケール差・軸の取り方・ボーン名の違いは
    /// Unity の Humanoid 正規化が吸収してくれる。
    /// </summary>
    public static class BvhHumanoid
    {
        /// <summary>
        /// BVH のジョイント名から Unity の Humanoid ボーンを推測するための表。
        ///
        /// BVH のジョイント名に決まりは無く、配布元ごとにばらばら（Mixamo 系の
        /// LeftUpLeg、CMU 系の lfemur、Neuron 系の LeftLeg など）。正規化した名前の
        /// 完全一致で引く。ここに無い名前は当たらないので、エントリ側の
        /// Bone Overrides で手当てする。
        /// </summary>
        private static readonly Dictionary<string, string[]> BoneSynonyms = new Dictionary<string, string[]>
        {
            ["Hips"] = new[] { "hips", "hip", "pelvis", "root", "hipjoint", "bip01pelvis", "mixamorighips" },
            ["Spine"] = new[] { "spine", "spine1", "abdomen", "lowerback", "chest0", "bip01spine" },
            ["Chest"] = new[] { "chest", "spine2", "upperback", "bip01spine1", "mixamorigspine1" },
            ["UpperChest"] = new[] { "upperchest", "spine3", "thorax", "mixamorigspine2" },
            ["Neck"] = new[] { "neck", "neck1", "bip01neck", "mixamorigneck" },
            ["Head"] = new[] { "head", "bip01head", "mixamorighead" },

            ["LeftShoulder"] = new[] { "leftshoulder", "lshoulder", "lcollar", "lclavicle", "leftcollar", "mixamorigleftshoulder" },
            ["RightShoulder"] = new[] { "rightshoulder", "rshoulder", "rcollar", "rclavicle", "rightcollar", "mixamorigrightshoulder" },
            ["LeftUpperArm"] = new[] { "leftupperarm", "leftarm", "lupperarm", "lhumerus", "larm", "mixamorigleftarm" },
            ["RightUpperArm"] = new[] { "rightupperarm", "rightarm", "rupperarm", "rhumerus", "rarm", "mixamorigrightarm" },
            ["LeftLowerArm"] = new[] { "leftlowerarm", "leftforearm", "lforearm", "lradius", "lelbow", "mixamorigleftforearm" },
            ["RightLowerArm"] = new[] { "rightlowerarm", "rightforearm", "rforearm", "rradius", "relbow", "mixamorigrightforearm" },
            ["LeftHand"] = new[] { "lefthand", "lhand", "lwrist", "mixamoriglefthand" },
            ["RightHand"] = new[] { "righthand", "rhand", "rwrist", "mixamorigrighthand" },

            ["LeftUpperLeg"] = new[] { "leftupleg", "leftupperleg", "lefthip", "lfemur", "lthigh", "lhip", "lupleg", "mixamorigleftupleg" },
            ["RightUpperLeg"] = new[] { "rightupleg", "rightupperleg", "righthip", "rfemur", "rthigh", "rhip", "rupleg", "mixamorigrightupleg" },
            ["LeftLowerLeg"] = new[] { "leftleg", "leftlowerleg", "leftknee", "ltibia", "lshin", "lknee", "lleg", "mixamorigleftleg" },
            ["RightLowerLeg"] = new[] { "rightleg", "rightlowerleg", "rightknee", "rtibia", "rshin", "rknee", "rleg", "mixamorigrightleg" },
            ["LeftFoot"] = new[] { "leftfoot", "lfoot", "lankle", "mixamorigleftfoot" },
            ["RightFoot"] = new[] { "rightfoot", "rfoot", "rankle", "mixamorigrightfoot" },
            ["LeftToes"] = new[] { "lefttoebase", "lefttoes", "ltoe", "ltoes", "lefttoe", "ltoebase", "mixamoriglefttoebase" },
            ["RightToes"] = new[] { "righttoebase", "righttoes", "rtoe", "rtoes", "righttoe", "rtoebase", "mixamorigrighttoebase" },
        };

        /// <summary>
        /// ジョイント名 -> Humanoid ボーン名 を推測する。
        /// </summary>
        /// <param name="overrides">
        /// 手当て。キーは BVH のジョイント名、値は Unity の Humanoid ボーン名。
        /// 値が空なら「このジョイントは割り当てない」の意味。自動推測より優先する。
        /// </param>
        public static Dictionary<string, string> BuildBoneMap(BvhFile file, IEnumerable<BvhBoneOverride> overrides)
        {
            var explicitMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (overrides != null)
            {
                foreach (BvhBoneOverride entry in overrides)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.jointName)) continue;
                    explicitMap[entry.jointName.Trim()] = entry.humanBoneName?.Trim() ?? string.Empty;
                }
            }

            // 正規化名 -> Humanoid ボーン名 の逆引きを組む。
            var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string[]> pair in BoneSynonyms)
            {
                foreach (string synonym in pair.Value)
                {
                    // 先勝ち。表の並び順が優先順位になる。
                    if (!lookup.ContainsKey(synonym)) lookup.Add(synonym, pair.Key);
                }
            }

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);

            foreach (BvhJoint joint in file.Joints)
            {
                if (joint.IsEndSite) continue;

                if (explicitMap.TryGetValue(joint.Name, out string forced))
                {
                    if (!string.IsNullOrEmpty(forced) && used.Add(forced)) map[joint.Name] = forced;
                    continue;
                }

                string normalized = Normalize(joint.Name);
                if (!lookup.TryGetValue(normalized, out string humanBone)) continue;

                // 同じ Humanoid ボーンに 2 つ割り当てると Avatar が組めない。先勝ちにする。
                if (used.Add(humanBone)) map[joint.Name] = humanBone;
            }

            return map;
        }

        /// <summary>
        /// 名前を突き合わせ用に均す。区切り・空白・接頭辞を落として小文字にする。
        /// "mixamorig:LeftUpLeg" と "Left_Up_Leg" と "leftupleg" を同じにするため。
        /// </summary>
        private static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            var builder = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
            }
            return builder.ToString();
        }

        /// <summary>
        /// 組んだ骨格から Humanoid Avatar を作る。失敗したら null を返し、理由を error に入れる。
        /// </summary>
        public static Avatar BuildAvatar(BvhSkeleton skeleton, Dictionary<string, string> boneMap, out string error)
        {
            error = null;

            var missing = new List<string>();
            for (int i = 0; i < HumanTrait.BoneCount; i++)
            {
                if (!HumanTrait.RequiredBone(i)) continue;

                string bone = HumanTrait.BoneName[i];
                if (!boneMap.ContainsValue(bone)) missing.Add(bone);
            }

            if (missing.Count > 0)
            {
                error = "Humanoid に必要なボーンを BVH から見つけられませんでした: "
                        + string.Join(", ", missing)
                        + "\n  エントリの Bone Overrides で BVH のジョイント名を割り当ててください。";
                return null;
            }

            var humanBones = new List<HumanBone>();
            foreach (KeyValuePair<string, string> pair in boneMap)
            {
                humanBones.Add(new HumanBone
                {
                    boneName = pair.Key,
                    humanName = pair.Value,
                    limit = new HumanLimit { useDefaultValues = true },
                });
            }

            var skeletonBones = new List<SkeletonBone>();
            CollectSkeletonBones(skeleton.Root.transform, skeletonBones);

            var description = new HumanDescription
            {
                human = humanBones.ToArray(),
                skeleton = skeletonBones.ToArray(),

                // Unity の既定値。0 のまま渡すと腕や脚が伸びなくなり、
                // リターゲット結果が原型と食い違う。
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false,
            };

            Avatar avatar = AvatarBuilder.BuildHumanAvatar(skeleton.Root, description);

            if (avatar == null || !avatar.isValid)
            {
                if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
                error = "BVH の骨格から Humanoid Avatar を作れませんでした。"
                        + "rest pose（OFFSET だけの姿勢）が T ポーズになっていない可能性があります。";
                return null;
            }

            avatar.name = skeleton.Root.name + " Avatar";
            return avatar;
        }

        /// <summary>Avatar に渡す骨格。ルートを含む全 Transform を rest pose の値で並べる。</summary>
        private static void CollectSkeletonBones(Transform transform, List<SkeletonBone> result)
        {
            result.Add(new SkeletonBone
            {
                name = transform.name,
                position = transform.localPosition,
                rotation = transform.localRotation,
                scale = transform.localScale,
            });

            for (int i = 0; i < transform.childCount; i++)
            {
                CollectSkeletonBones(transform.GetChild(i), result);
            }
        }

        /// <summary>推測結果を人が読める形にする。Console へ出して確認してもらうため。</summary>
        public static string DescribeBoneMap(BvhFile file, Dictionary<string, string> boneMap)
        {
            var builder = new StringBuilder();
            builder.Append($"BVH のジョイント {file.Joints.Count(j => !j.IsEndSite)} 個のうち "
                           + $"{boneMap.Count} 個を Humanoid ボーンへ割り当てました。");

            string[] unmapped = file.Joints
                .Where(j => !j.IsEndSite && !boneMap.ContainsKey(j.Name))
                .Select(j => j.Name)
                .ToArray();

            if (unmapped.Length > 0)
            {
                builder.Append("\n  未割り当て: " + string.Join(", ", unmapped));
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// 自動推測を上書きするための 1 件。
    /// <see cref="humanBoneName"/> が空なら「割り当てない」。
    /// </summary>
    [Serializable]
    public class BvhBoneOverride
    {
        [Tooltip("BVH side joint name")]
        public string jointName;

        [Tooltip("Unity humanoid bone name (empty = leave unmapped)")]
        public string humanBoneName;
    }
}
