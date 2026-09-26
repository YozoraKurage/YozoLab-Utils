using JetBrains.Annotations;
using YozoLab.SPS.Builder;

namespace YozoLab.SPS.Utils {
    internal static class SimpleObjectExtensions {
        /**
         * Converts destroyed Objects ("fake-null") into "real" null values which can use null-coalescing
         */
        [CanBeNull]
        public static T NullSafe<T>([CanBeNull] this T obj) where T : UnityEngine.Object {
            return obj == null ? null : obj;
        }
        [CanBeNull]
        public static VFGameObject NullSafe([CanBeNull] this VFGameObject obj) {
            return obj == null ? null : obj;
        }
    }
}
