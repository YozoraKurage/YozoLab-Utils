using YozoLab.SPS.Inspector;
using JetBrains.Annotations;
using UnityEditor;
using YozoLab.SPS.Hooks;
using YozoLab.SPS.Utils;

namespace YozoLab.SPS.Menu {
    internal static class MenuUtils {
        [CanBeNull]
        public static VFGameObject GetSelectedAvatar() {
            var obj = Selection.activeGameObject.asVf();
            if (obj == null) return null;
            return obj.GetAvatarRoot();
        }
    }
}
