using JetBrains.Annotations;
using UnityEngine;

namespace YozoLab.SPS.Utils {
    internal static class ComponentExtensions {
        public static void Destroy(this UnityEngine.Component c) {
            if (c.owner().GetComponents().Length == 2 && c.owner().childCount == 0)
                c.owner().Destroy();
            else
                Object.DestroyImmediate(c);
        }
    }
}
