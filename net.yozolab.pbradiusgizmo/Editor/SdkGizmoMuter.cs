// VRChat SDK（com.vrchat.base）がある環境でだけコンパイルされる。
#if YOZOLAB_PBRADIUS_VRCSDK
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;

namespace YozoLab.PBRadiusGizmo
{
    /// <summary>
    /// ドラッグ中だけ、その 1 体の SDK ギズモを止める（Accelerator が動いていないとき用）。
    ///
    /// VRCPhysBoneEditor.Draw は冒頭で showGizmos を読んで false なら即 return する。
    /// そこをドラッグの間だけ伏せる。showGizmos はシリアライズされる値なので、
    /// 素のフィールド代入（Undo にも dirty にも載せない）で書き、終わったら必ず戻す。
    /// ドラッグは瞬間的な操作だが、途中でリロードが走っても値が残らないよう保険を張る。
    ///
    /// Accelerator（VRC Gizmo Accelerator）が動いている間は SDK ギズモ自体が
    /// 止まっているので、これは使わない。
    /// </summary>
    [InitializeOnLoad]
    internal static class SdkGizmoMuter
    {
        private static VRCPhysBoneBase _muted;
        private static bool _original;

        static SdkGizmoMuter()
        {
            AssemblyReloadEvents.beforeAssemblyReload += End;
            EditorApplication.quitting += End;
        }

        internal static void Begin(VRCPhysBoneBase pb)
        {
            End();
            if (pb == null) return;

            _muted = pb;
            _original = pb.showGizmos;
            pb.showGizmos = false;
            SceneView.RepaintAll();
        }

        internal static void End()
        {
            if (_muted == null) { _muted = null; return; }

            _muted.showGizmos = _original;
            _muted = null;
            SceneView.RepaintAll();
        }
    }
}
#endif
