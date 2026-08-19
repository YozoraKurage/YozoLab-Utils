using UnityEngine;
using UnityEditor;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// VRC Gizmo Accelerator の設定。
    ///
    /// パッケージ更新で配下が入れ替わっても設定が消えないよう、
    /// ProjectSettings/ 側に保存する（他の YozoLab ツールと同じ方針）。
    /// </summary>
    [FilePath("ProjectSettings/VRCGizmoAcceleratorSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class VRCGizmoAcceleratorSettings : ScriptableSingleton<VRCGizmoAcceleratorSettings>
    {
        [Tooltip("Master switch. OFF by default: nothing is patched and the SDK draws its gizmos as usual")]
        public bool enabled = false;

        [Tooltip("Also draw PhysBones unrelated to the selection, at half opacity (matches the SDK's always-on drawing). Costs proportionally more")]
        public bool drawUnselected = false;

        public void SaveSettings()
        {
            Save(true);
        }
    }
}
