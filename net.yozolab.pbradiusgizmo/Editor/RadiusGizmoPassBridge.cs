// VRC Gizmo Accelerator への橋渡し。
//
// Accelerator は同じリポジトリの別アドオンで、独立して有効・無効にできる。
// asmdef で参照してしまうと、Accelerator を切ったときにこちらが道連れで
// コンパイルできなくなる（あるいは参照が外れて CS0246 になる）。
// なので参照は持たず、実行時にリフレクションで探す。
//
// 見つからない = Accelerator が入っていないか切られている。そのときは
// PassActive が false のままなので、呼び出し側は SDK ギズモを直接伏せる
// 従来の経路（SdkGizmoMuter）へ落ちる。
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace YozoLab.PBRadiusGizmo
{
    /// <summary>
    /// Accelerator の代替パスに対して「ドラッグ中の PhysBone は既定形状を描くな」と
    /// 伝える橋渡し。旧実装では Harmony で SDK のギズモ入口を止めていたが、
    /// Accelerator が動いているときは SDK ギズモ自体が既に止まっており、
    /// 消すべきは Accelerator 側の代替形状になる。
    ///
    /// 描き足しはしない。オーバーレイとハンドルは PhysBoneRadiusGizmo が
    /// これまでどおり自前で描く（対話は SceneView の GUI イベントが要るため）。
    /// </summary>
    [InitializeOnLoad]
    internal static class RadiusGizmoPassBridge
    {
        private const string PassTypeName = "YozoLab.VRCGizmoAccelerator.PhysBoneGizmoPass";

        private static readonly PropertyInfo ActiveProperty;
        private static readonly MethodInfo InvalidateMethod;

        static RadiusGizmoPassBridge()
        {
            Type pass = FindPassType();
            if (pass == null) return;

            ActiveProperty = pass.GetProperty("Active", BindingFlags.Public | BindingFlags.Static);
            InvalidateMethod = pass.GetMethod("Invalidate", BindingFlags.Public | BindingFlags.Static,
                                              null, Type.EmptyTypes, null);

            // 既定形状を伏せる判定を差し込む。Func<Component, bool> は双方が
            // 素で名前を書ける型なので、デリゲート型を作り直さずそのまま入る。
            FieldInfo hook = pass.GetField("SuppressDefaultFor", BindingFlags.Public | BindingFlags.Static);
            if (hook != null && hook.FieldType == typeof(Func<Component, bool>))
            {
                hook.SetValue(null, (Func<Component, bool>)PhysBoneRadiusGizmo.IsDragging);
            }
        }

        /// <summary>読み込まれているアセンブリから代替パスの公開面を探す。</summary>
        private static Type FindPassType()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type found = assembly.GetType(PassTypeName, false);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Accelerator の代替パスが動いているか。居なければ false。</summary>
        internal static bool PassActive
        {
            get
            {
                if (ActiveProperty == null) return false;
                try
                {
                    return (bool)ActiveProperty.GetValue(null);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>ドラッグの開始・終了で代替パスに組み立て直させる。</summary>
        internal static void InvalidatePass()
        {
            if (InvalidateMethod == null) return;
            try
            {
                InvalidateMethod.Invoke(null, null);
            }
            catch (Exception)
            {
                // パスが消えていても、こちらの描画は止めない。
            }
        }
    }
}
