// VRC Gizmo Accelerator が有効なとき（YOZOLAB_HAS_VRCGIZMOACC は util-settings が
// asmdef へ注入する）だけコンパイルされる、代替ギズモパスへの橋渡し。
#if YOZOLAB_PBRADIUS_VRCSDK && YOZOLAB_HAS_VRCGIZMOACC
using UnityEditor;
using UnityEngine;
using YozoLab.VRCGizmoAccelerator;

namespace YozoLab.PBRadiusGizmo
{
    /// <summary>
    /// Accelerator の代替パスに対して「ドラッグ中の PhysBone は既定形状を描くな」と
    /// 伝える拡張。旧実装では Harmony で SDK のギズモ入口を止めていたが、
    /// Accelerator が動いているときは SDK ギズモ自体が既に止まっており、
    /// 消すべきは Accelerator 側の代替形状になる。
    ///
    /// 描き足しはしない。オーバーレイとハンドルは PhysBoneRadiusGizmo が
    /// これまでどおり自前で描く（対話は SceneView の GUI イベントが要るため）。
    /// </summary>
    [InitializeOnLoad]
    internal static class RadiusGizmoPassBridge
    {
        private sealed class Extension : IPhysBoneGizmoExtension
        {
            public int Order => 0;

            public void Build(Component physBone, PhysBoneGizmoCanvas canvas)
            {
                if (PhysBoneRadiusGizmo.IsDragging(physBone))
                    canvas.SuppressDefault = true;
            }
        }

        static RadiusGizmoPassBridge()
        {
            PhysBoneGizmoPass.Register(new Extension());
        }

        /// <summary>Accelerator の代替パスが動いているか。</summary>
        internal static bool PassActive => PhysBoneGizmoPass.Active;

        /// <summary>ドラッグの開始・終了で代替パスに組み立て直させる。</summary>
        internal static void InvalidatePass() => PhysBoneGizmoPass.Invalidate();
    }
}
#endif
