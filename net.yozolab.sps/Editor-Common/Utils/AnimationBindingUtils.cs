using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Model.Feature;
using YozoLab.SPS.Utils.Controller;
#if VRCSDK_HAS_VRCCONSTRAINTS
using VRC.SDKBase.Validation.Performance;
#endif

namespace YozoLab.SPS.Utils {
    internal static class AnimationBindingUtils {
        internal static VFGameObject ResolveTarget(
            VFLoadContext context,
            string path,
            Type type
        ) {
            var ownerObject = context.OwnerObject;
            var animatorObject = context.AnimatorObject;
            var objectPaths = context.ObjectPaths;
            var reverseObjectPaths = context.ReverseObjectPaths;
            if (animatorObject == null) return null;
            if (ownerObject == null) return null;
            if (path == null) return null;
            if (path == "" && context.RootBindingsApplyToAvatar) {
                return animatorObject;
            }
            if (path.StartsWith("/")) {
                var target = objectPaths.Find(animatorObject, path.TrimStart('/'), reverseObjectPaths);
                return IsValidResolvedTarget(target, type, animatorObject) ? target : null;
            }

            var ancestor = ownerObject;
            while (ancestor != null && ancestor != animatorObject) {
                ancestor = objectPaths.GetParent(ancestor, reverseObjectPaths);
            }
            if (ancestor != animatorObject) return null;

            VFGameObject current = ownerObject;
            while (current != null) {
                var target = objectPaths.Find(current, path, reverseObjectPaths);
                if (IsValidResolvedTarget(target, type, animatorObject)) {
                    return target;
                }

                if (current == animatorObject) break;
                current = objectPaths.GetParent(current, reverseObjectPaths);
            }
            return null;
        }


        internal static bool IsValidResolvedTarget(VFGameObject target, Type type, VFGameObject bindingRoot) {
            if (target == null) return false;
            if (!target.IsSameOrChildOf(bindingRoot)) return false;
            if (type == null) return false;
            if (type == typeof(GameObject)) return true;
            if (type == typeof(Animator)) return true;
            if (!typeof(UnityEngine.Component).IsAssignableFrom(type)) return false;
            if (target.GetComponent(type) != null) return true;

            if (type == typeof(BoxCollider)
                && target.GetComponents().Any(component => component.GetType().Name == "VRCStation")) return true;
#if VRCSDK_HAS_VRCCONSTRAINTS
            // Half-upgraded assets can temporarily point at the other kind of constraint.
            if (typeof(IConstraint).IsAssignableFrom(type)
                && target.GetComponents<IVRCConstraint>().Any()) return true;
            if (typeof(IVRCConstraint).IsAssignableFrom(type)
                && target.GetComponents<IConstraint>().Any()) return true;
#endif
            return false;
        }

        internal static string ResolveRelativePath(string a, string b) {
            if (string.IsNullOrEmpty(b)) return a;
            var output = new List<string>();
            if (!b.StartsWith("/") && !string.IsNullOrEmpty(a)) {
                output.AddRange(a.Split('/'));
            }
            foreach (var part in b.Split('/')) {
                if (part == "..") {
                    if (output.Count == 0) return null;
                    output.RemoveAt(output.Count - 1);
                } else if (part == ".") {
                } else if (part != "") {
                    output.Add(part);
                }
            }
            return string.Join("/", output);
        }
    }
}
