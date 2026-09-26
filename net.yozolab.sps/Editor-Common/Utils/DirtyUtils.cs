using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Model;

namespace YozoLab.SPS.Utils {
    internal static class DirtyUtils {
        public static void Dirty(this Object obj) {
            EditorUtility.SetDirty(obj);
            if (PrefabUtility.IsPartOfPrefabInstance(obj))
                PrefabUtility.RecordPrefabInstancePropertyModifications(obj);

            // This shouldn't be needed in unity 2020+
#if ! UNITY_2022_1_OR_NEWER
            if (obj is GameObject go) {
                MarkSceneDirty(go.scene);
            } else if (obj is UnityEngine.Component c) {
                MarkSceneDirty(c.owner().scene);
            }
#endif
        }

        private static void MarkSceneDirty(Scene scene) {
            if (Application.isPlaying) return;
            if (scene == null) return;
            if (!scene.isLoaded) return;
            if (!scene.IsValid()) return;
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
