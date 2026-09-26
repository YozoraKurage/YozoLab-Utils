using System.Linq;
using UnityEditor;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Builder.Haptics;
using YozoLab.SPS.Component;
using YozoLab.SPS.Exceptions;
using YozoLab.SPS.Utils;

namespace YozoLab.SPS.Menu {
    internal static class SpsMenuItem {
        [MenuItem("GameObject/YozoLab SPS/Create SPS Socket", priority = 40)]
        [MenuItem(MenuItems.createSocket, priority = MenuItems.createSocketPriority)]
        public static void RunSocket() {
            VRCFuryBuildContext.Run(() => {
                Create(false);
            });
        }

        [MenuItem("GameObject/YozoLab SPS/Create SPS Plug", priority = 41)]
        [MenuItem(MenuItems.createPlug, priority = MenuItems.createPlugPriority)]
        public static void RunPlug() {
            VRCFuryBuildContext.Run(() => {
                Create(true);
            });
        }

        private const string DialogTitle = "YozoLab SPS";

        private static void Create(bool plug) {
            var newObj = GameObjects.Create(plug ? "SPS Plug" : "SPS Socket", Selection.activeTransform);

            if (plug) {
                newObj.AddComponent<SpsPlug>();
            } else {
                newObj.AddComponent<SpsSocket>();
            }

            Tools.pivotRotation = PivotRotation.Local;
            Tools.pivotMode = PivotMode.Pivot;
            Tools.current = Tool.Move;
            Selection.SetActiveObjectWithContext(newObj, newObj);
            //SceneView.FrameLastActiveSceneView();

            DialogUtils.DisplayDialog(DialogTitle,
                $"{(plug ? "Plug" : "Socket")} created!\n\nDon't forget to attach it to an appropriate bone on your avatar and rotate it so it faces the correct direction!", "Ok");

            var sv = EditorWindowFinder.GetWindows<SceneView>().FirstOrDefault();
            if (sv != null) sv.drawGizmos = true;
        }
    }
}