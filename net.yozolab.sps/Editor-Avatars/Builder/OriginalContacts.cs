using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using YozoLab.SPS.Utils;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace YozoLab.SPS.Builder {
    /**
     * ビルドの最初に、アバターの指などの当たり判定が使っている Transform を記録しておく。
     * 後で SPS の送信側を置くときに、当たり判定の位置が途中で変わっていないかを見るため。
     *
     * VRCFury では VRCSDK のビルドフックとして動いていたが、ここでは SPS の NDMF の処理の
     * 最初に呼ぶ。あわせて、VRCSDK がアバターディスクリプタのインスペクタを開いたときに行う
     * 当たり判定の更新を、先に済ませておく（開いていないと値が古いままのことがあるため）。
     */
    internal static class OriginalContacts {
        
        private abstract class Reflection : ReflectionHelper {
            public static readonly Type AvatarDescriptorEditor3 = ReflectionUtils.GetTypeFromAnyAssembly("AvatarDescriptorEditor3");
            public static readonly MethodInfo UpdateAutoColliders = AvatarDescriptorEditor3?.VFMethod("UpdateAutoColliders");
            public static readonly MethodInfo MirrorCollider = AvatarDescriptorEditor3?.VFMethod("MirrorCollider");
            public static readonly FieldInfo avatarDescriptor = AvatarDescriptorEditor3?.VFField("avatarDescriptor");
            public static readonly FieldInfo[] ColliderFields = typeof(VRCAvatarDescriptor).GetFields()
                .Where(f => f.FieldType == typeof(VRCAvatarDescriptor.ColliderConfig))
                .ToArray();
        }

        public static Exception fixException = null;
        public static readonly ISet<Transform> usedTransforms = new HashSet<Transform>();

        public static void Record(VFGameObject go) {
            if (!ReflectionHelper.IsReady<Reflection>()) return;

            var avatar = go.GetComponent<VRCAvatarDescriptor>();
            if (avatar != null) {
                fixException = null;
                try {
                    FixInvalidDescriptorColliderInfo(avatar);
                } catch (Exception e) {
                    fixException = e;
                    Debug.LogError(e);
                }

                RecordUsedTransforms(avatar);
            }
        }

        /**
         * The finger collider fields can be wrong if the user hasn't opened the avatar descriptor colliders editor recently,
         * because it only updates the transforms when that property drawer is shown. This is a VRCSDK issue, and can result in
         * the colliders being unset or set improperly, which breaks things later like global collider finger detection.
         * We force the VRCSDK to update the global contacts immediately upon avatar build start to resolve this issue.
         */
        private static void FixInvalidDescriptorColliderInfo(VRCAvatarDescriptor avatar) {
            if (!ReflectionHelper.IsReady<Reflection>()) {
                throw new Exception("Collider fix methods could not be found, maybe VRCF doesn't support this VRCSDK version?");
            }

            var editor = Editor.CreateEditor(avatar);
            try {
                if (!Reflection.AvatarDescriptorEditor3.IsInstanceOfType(editor)) {
                    throw new Exception("Avatar descriptor editor was not a AvatarDescriptorEditor3");
                }

                Reflection.avatarDescriptor.SetValue(editor, avatar);
                Reflection.UpdateAutoColliders.Invoke(editor, new object[] { });

                foreach (var f in Reflection.ColliderFields) {
                    var collider = (VRCAvatarDescriptor.ColliderConfig)f.GetValue(avatar);
                    if (collider.isMirrored && f.Name.EndsWith("L") && Reflection.MirrorCollider != null && Reflection.MirrorCollider.GetParameters().Length == 2) {
                        var so = new SerializedObject(avatar);
                        var leftProp = so.FindProperty(f.Name);
                        var rightProp = so.FindProperty(f.Name.Substring(0, f.Name.Length - 1) + "R");
                        if (leftProp != null && rightProp != null) {
                            Reflection.MirrorCollider.Invoke(editor, new object[] { leftProp, rightProp });
                            // In case harmony isn't present so this didn't already get fixed
                            so.ApplyModifiedPropertiesWithoutUndo();
                        }
                    }
                }
            } finally {
                Object.DestroyImmediate(editor);
            }
        }

        private static void RecordUsedTransforms(VRCAvatarDescriptor avatar) {
            usedTransforms.Clear();
            foreach (var f in Reflection.ColliderFields) {
                var collider = (VRCAvatarDescriptor.ColliderConfig)f.GetValue(avatar);
                if (collider.transform != null) {
                    usedTransforms.Add(collider.transform);
                }
            }
        }
    }
}
