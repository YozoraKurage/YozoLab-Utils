// この機能は Harmony（0Harmony.dll）に依存します。
// Harmony を同梱する VRCSDK（com.vrchat.base）がある環境でだけ中身がコンパイルされ、
// 判定は asmdef の versionDefines に任せています（手動設定は不要）。
// シンボル "YOZOLAB_PARTICLETOOLS_HARMONY" はこのアセンブリのコンパイル時のみ有効で、
// プロジェクトの Scripting Define Symbols には何も書き込みません。
// Harmony が無い環境ではこのクラスは何もしない殻になり、コンパイルは通ります。
#if YOZOLAB_PARTICLETOOLS_HARMONY
using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
#endif

namespace YozoLab.ParticleTools
{
    /// <summary>
    /// Unity 標準の Particle Effect パネル（Scene ビュー右下のオーバーレイ）を、
    /// こちらが同じエフェクトを掴んでいる間だけ隠す。
    ///
    /// 狙いは 2 つ。
    /// 1. このパッケージは標準パネルの置き換えなので、二重に出ていても邪魔なだけ。
    /// 2. 標準パネルは描画のたびに CalculateEffectUIData と
    ///    CalculateEffectUISubEmitterData を全選択システムに掛ける。後者は
    ///    サブエミッタの粒を数えて回るので、サブエミッタが太いエフェクトでは
    ///    Scene ビューの再描画ごとに無視できない負荷になる。隠せば丸ごと省ける。
    ///
    /// パネル本体は UnityEditor.ParticleEffectUI の入れ子クラス
    /// SceneViewParticleOverlay で、visible が false なら OnGUI ごと呼ばれない。
    /// そこへ postfix を足すだけなので、モジュールの Scene ビューハンドル
    /// （Shape のギズモなど）には一切触らない。
    /// </summary>
    internal static class BuiltinPanelSuppressor
    {
#if YOZOLAB_PARTICLETOOLS_HARMONY
        private const string HarmonyId = "net.yozolab.particletools.builtinpanel";
        private const string OverlayTypeName = "UnityEditor.ParticleEffectUI+SceneViewParticleOverlay";

        private static Harmony harmony;
        private static bool warned;

        /// <summary>パッチが当たっているか（＝標準パネルを隠せる状態か）。</summary>
        internal static bool Available => harmony != null;

        /// <summary>当てられなかったときの理由。当たっていれば null。</summary>
        internal static string UnavailableReason { get; private set; }

        /// <summary>冪等。何度呼んでもよい。</summary>
        internal static void Install()
        {
            if (harmony != null || UnavailableReason != null) return;

            try
            {
                // 型探しは BuiltinPreviewBridge と共用する(エディタのアセンブリ分割は
                // バージョンで変わるので、UnityEditor.Editor と同じ所にあるとは限らない)。
                Type overlayType = BuiltinPreviewBridge.FindEditorType(OverlayTypeName);

                // DeclaredOnly が肝。override が無くなった将来の Unity で基底の
                // Overlay.visible を掴んでしまうと、標準パネルどころか
                // このオーバーレイまで含めた全オーバーレイが消える。
                // そこは黙って壊れるより、当てずに警告で済ませる。
                MethodInfo target = overlayType
                    ?.GetProperty("visible", BindingFlags.Instance | BindingFlags.Public
                                             | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    ?.GetGetMethod(true);

                if (target == null)
                {
                    Fail($"{OverlayTypeName}.visible が見つかりません（Unity の構成が変わった可能性）");
                    return;
                }

                var instance = new Harmony(HarmonyId);
                instance.Patch(target, postfix: new HarmonyMethod(
                    typeof(BuiltinPanelSuppressor).GetMethod(
                        nameof(Visible_Postfix), BindingFlags.Static | BindingFlags.NonPublic)));

                harmony = instance;
            }
            catch (Exception e)
            {
                Fail(e.Message);
            }
        }

        internal static void Uninstall()
        {
            UnavailableReason = null;
            warned = false;
            if (harmony == null) return;

            try { harmony.UnpatchAll(HarmonyId); }
            catch (Exception e)
            {
                Debug.LogWarning($"[Particle Tools] パッチを外せませんでした: {e.Message}");
            }
            harmony = null;
        }

        private static void Fail(string reason)
        {
            UnavailableReason = reason;
            if (warned) return;
            warned = true;
            Debug.LogWarning(
                $"[Particle Tools] 標準の Particle Effect パネルを隠せませんでした: {reason}。"
                + "標準パネルとこのオーバーレイが並んで表示されます。");
        }

        // visible が true でも、こちらが掴んでいる間は false にして描画ごと省く。
        private static void Visible_Postfix(ref bool __result)
        {
            if (__result && ParticleScrubController.SuppressesBuiltinPanel) __result = false;
        }
#else
        internal static bool Available => false;

        internal static string UnavailableReason =>
            "Harmony (0Harmony.dll) が見つかりません";

        internal static void Install() { }

        internal static void Uninstall() { }
#endif
    }
}
