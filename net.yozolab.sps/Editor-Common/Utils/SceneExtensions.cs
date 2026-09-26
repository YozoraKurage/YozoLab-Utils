using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;

namespace YozoLab.SPS.Utils {
    internal static class SceneExtensions {
        public static VFGameObject[] Roots(this Scene scene) {
            return scene.GetRootGameObjects().AsVf().ToArray();
        }
    }
}