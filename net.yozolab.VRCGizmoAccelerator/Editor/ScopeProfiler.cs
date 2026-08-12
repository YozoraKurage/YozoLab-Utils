using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Profiling;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// パッチした入口に、Unity のプロファイラから見えるマーカーを差し込む。
    ///
    /// IMGUIContainer.OnGUI や GizmoManager.DrawGizmos の中身は、どの
    /// エディタが何 ms 使っているかが外からは分からない。ここで名前を付けて
    /// おけば、プロファイラの階層に "YozoLab …" として直接出るので、
    /// 推測せずに切り分けられる。
    /// </summary>
    internal static class ScopeProfiler
    {
        private static readonly Dictionary<MethodBase, string> Labels =
            new Dictionary<MethodBase, string>();

        internal static void Register(MethodBase method, string label)
        {
            if (method == null) return;
            Labels[method] = "YozoLab " + label;
        }

        internal static void Clear() => Labels.Clear();

        internal static void Begin(MethodBase method)
        {
            if (!VRCGizmoAcceleratorSettings.instance.profilerMarkers) return;

            Profiler.BeginSample(
                method != null && Labels.TryGetValue(method, out var label)
                    ? label
                    : "YozoLab Gizmo");
        }

        internal static void End()
        {
            if (!VRCGizmoAcceleratorSettings.instance.profilerMarkers) return;
            Profiler.EndSample();
        }
    }
}
