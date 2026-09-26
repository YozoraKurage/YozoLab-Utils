using System.Linq;
using UnityEngine;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Model;
using YozoLab.SPS.Model.Feature;
using VRC.SDKBase;

namespace YozoLab.SPS.Utils {
    internal static class EditorOnlyUtils {

        public static void RemoveEditorOnlyObjects(VFGameObject gameObject) {
            foreach (var child in gameObject.Children()) {
                if (IsEditorOnly(child)) {
                    child.Destroy();
                } else {
                    RemoveEditorOnlyObjects(child);
                }
            }
        }
        
        public static bool IsInsideEditorOnly(VFGameObject gameObject) {
            return gameObject.GetSelfAndAllParents().Any(IsEditorOnly);
        }

        private static bool IsEditorOnly(VFGameObject obj) {
            if (obj.HasTag("EditorOnly")) {
                return true;
            }
            return false;
        }
        
        public static void RemoveEditorOnlyComponents(VFGameObject gameObject) {
#if VRC_NEW_HOOK_API
            foreach (var e in gameObject.GetComponentsInSelfAndChildren<IEditorOnly>()) {
                if (e is UnityEngine.Component c) {
                    Object.DestroyImmediate(c);
                }
            }
#endif
        }
    }
}
