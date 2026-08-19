// VRChat SDK（com.vrchat.base）がある環境でだけコンパイルされる。
// Harmony（0Harmony.dll）も SDK 同梱のものを使う。判定は asmdef の versionDefines に
// 任せてあるので、手動設定は要らない。
#if YOZOLAB_VRCGIZMOACC_VRCSDK
using System;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEngine;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// SDK 自身の PhysBone ギズモ描画を Harmony で握りつぶす。
    ///
    /// VRCPhysBoneEditor.OnDrawGizmos は [DrawGizmo] で呼ばれる唯一の入口なので、
    /// prefix で false を返せば InitTransforms（アバター全体の走査を伴う一番重い
    /// 前処理）ごと丸ごと飛ぶ。
    ///
    /// showGizmos フィールドには一切触れない。あれはシリアライズされるユーザーの
    /// 設定であり、書き換えるとインスペクタのトグルが効かなくなるうえ、保存時の
    /// 復元という面倒を抱え込む。値はユーザーのものとしてそのまま残し、
    /// 代替パスが「描くかどうか」の判断に読むだけにする。
    /// </summary>
    internal static class SdkGizmoSuppressor
    {
        private const string HarmonyId = "net.yozolab.vrcgizmoaccelerator";
        private const string EditorTypeName = "VRC.SDK3.Dynamics.PhysBone.VRCPhysBoneEditor";

        private static Harmony _harmony;
        private static bool _warned;

        /// <summary>パッチが当たっているか（＝SDK のギズモが止まっているか）。</summary>
        internal static bool Installed => _harmony != null;

        /// <summary>当てられなかったときの理由。当たっていれば null。</summary>
        internal static string UnavailableReason { get; private set; }

        /// <summary>冪等。毎フレーム呼ばれてよい。</summary>
        internal static void Install()
        {
            if (_harmony != null || UnavailableReason != null) return;

            try
            {
                Type editorType = AccessTools.TypeByName(EditorTypeName);
                MethodInfo target = editorType == null
                    ? null
                    : AccessTools.Method(editorType, "OnDrawGizmos");

                if (target == null)
                {
                    Fail($"{EditorTypeName}.OnDrawGizmos が見つかりません（SDK の構成が変わった可能性）");
                    return;
                }

                var harmony = new Harmony(HarmonyId);
                harmony.Patch(target, prefix: new HarmonyMethod(
                    typeof(SdkGizmoSuppressor).GetMethod(
                        nameof(OnDrawGizmos_Prefix), BindingFlags.Static | BindingFlags.NonPublic)));

                _harmony = harmony;
            }
            catch (Exception e)
            {
                Fail(e.Message);
            }
        }

        internal static void Uninstall()
        {
            UnavailableReason = null;
            _warned = false;
            if (_harmony == null) return;

            try { _harmony.UnpatchAll(HarmonyId); }
            catch (Exception e)
            {
                Debug.LogWarning($"[VRC Gizmo Accelerator] パッチを外せませんでした: {e.Message}");
            }
            _harmony = null;
        }

        private static void Fail(string reason)
        {
            UnavailableReason = reason;
            if (_warned) return;
            _warned = true;
            Debug.LogWarning(
                $"[VRC Gizmo Accelerator] SDK のギズモを止められませんでした: {reason}。"
                + "代替パスの表示と SDK の表示が重なります。");
        }

        // false を返すと元の OnDrawGizmos が走らない。
        private static bool OnDrawGizmos_Prefix() => false;
    }
}
#endif
