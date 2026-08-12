using UnityEngine;
using UnityEditor;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// 束ねたギズモをどうやって画面に出すか。
    /// </summary>
    public enum GizmoDrawMode
    {
        /// <summary>GL / Graphics.DrawMeshNow で即時に描く（既定）。</summary>
        Immediate = 0,

        /// <summary>
        /// カメラのコマンドバッファへメッシュを積む（既定）。
        /// Unity のギズモにも即時描画にも依存せず、色もそのまま出る。
        /// </summary>
        CommandBuffer = 3,

        /// <summary>
        /// 溜めた線を Gizmos.DrawLine で Unity のギズモレンダラへ渡す。
        /// 即時描画をしないので、IMGUI の描画分断も SetPass の発行も起きない。
        /// </summary>
        GizmoLines = 2,
    }

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

        [Tooltip("Accelerate PhysBone / PhysBone Collider gizmos")]
        public bool physBone = true;

        [Tooltip("Accelerate Contact Sender / Receiver gizmos")]
        public bool contact = true;

        [Tooltip("Accelerate VRC Constraint gizmos")]
        public bool constraint = true;

        [Tooltip("Accelerate the Avatar Descriptor collider drawing (OnSceneGUI, not a gizmo callback)")]
        public bool avatarDescriptor = true;

        [Tooltip("Skip rebuilding the PhysBone bone structure while nothing has changed. This is the single largest cost of PhysBone gizmos")]
        public bool cacheBoneInit = true;

        [Tooltip("How to submit the batched geometry. GizmoLines hands it to Unity's own gizmo renderer (no immediate drawing); Immediate uses GL directly")]
        public GizmoDrawMode drawMode = GizmoDrawMode.CommandBuffer;

        [Tooltip("Draw the whole gizmo pass in one go instead of once per component. Costs one frame of latency")]
        public bool combineDrawCalls = true;

        [Tooltip("Reuse the geometry drawn last frame while nothing has changed, instead of letting the SDK rebuild it. Costs memory (one mesh per component)")]
        public bool cacheGeometry = true;

        [Tooltip("Add Unity profiler markers (\"YozoLab ...\") around each patched entry point, so the cost shows up per component in the profiler")]
        public bool profilerMarkers = true;

        [Tooltip("Also intercept UnityEditor.Handles primitives. Cuts SetPass calls further, but Unity tessellates arcs natively while we have to redo it in C# - measure before turning this on")]
        public bool interceptUnityHandles = false;

        [Tooltip("Drop drawing calls that happen outside a repaint (Layout / MouseMove). Nothing is visible in those events, so the work is wasted")]
        public bool skipNonDrawingEvents = true;



        public bool IsGroupEnabled(string group)
        {
            switch (group)
            {
                case "PhysBone": return physBone;
                case "Contact": return contact;
                case "Constraint": return constraint;
                case "Avatar": return avatarDescriptor;
                default: return true;
            }
        }

        public void SaveSettings()
        {
            Save(true);
        }
    }
}
