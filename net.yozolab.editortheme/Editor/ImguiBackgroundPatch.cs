// この機能は Harmony（0Harmony.dll）に依存します。
// Harmony を同梱する VRCSDK（com.vrchat.base）がある環境でだけコンパイルされ、
// 無い環境ではこのファイルは空になり、コンパイルは通ります（窓の地色だけ Unity のままになる）。
#if YOZOLAB_EDITORTHEME_HARMONY
using System;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEngine;

namespace YozoLab.EditorTheme
{
    /// <summary>
    /// IMGUI ウィンドウの地色を差し替える。
    ///
    /// IMGUI で描かれるウィンドウ（Inspector、Hierarchy、Project、Preferences、Animator …）の
    /// 背景は GUISkin でも USS でもなく、<c>EditorGUIUtility.GetDefaultBackgroundColor()</c> が
    /// 返す 1 色（Dark で #383838）で塗られている。実機で測ると、この値の元になる
    /// 静的フィールドは無く、メソッドの中で決まっている。書き換えるには戻り値を
    /// 差し替えるしかないので Harmony で Postfix する。
    ///
    /// 無効化では Unpatch して元に戻す。
    /// </summary>
    internal static class ImguiBackgroundPatch
    {
        private const string HarmonyId = "net.yozolab.editortheme.background";
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        private static Harmony harmony;
        private static Color? background;
        private static bool translateDark = true;

        public static bool IsPatched => harmony != null;

        public static void Apply(Color windowBackground, bool dark)
        {
            background = windowBackground;
            translateDark = dark;
            if (harmony != null) return;

            harmony = new Harmony(HarmonyId);

            MethodInfo defaultBackground = typeof(EditorGUIUtility).GetMethod("GetDefaultBackgroundColor", Any);
            if (defaultBackground != null)
            {
                harmony.Patch(defaultBackground, postfix: new HarmonyMethod(typeof(ImguiBackgroundPatch), nameof(BackgroundPostfix)));
            }

            // IMGUI 側の色表（EditorResources.styleCatalog）は SVC<Color>.value を通して読まれる。
            // 読み出しのたびに翻訳表を通せば、キャッシュの有無に関わらず Iceberg になる。
            Type svcOpen = typeof(EditorGUIUtility).Assembly.GetType("UnityEditor.StyleSheets.SVC`1");
            MethodInfo svcValue = svcOpen?.MakeGenericType(typeof(Color)).GetProperty("value", Any)?.GetGetMethod(true);
            if (svcValue != null)
            {
                harmony.Patch(svcValue, postfix: new HarmonyMethod(typeof(ImguiBackgroundPatch), nameof(TranslatePostfix)));
            }

            if (defaultBackground == null && svcValue == null)
            {
                Debug.LogWarning("[YozoLab Editor Theme] IMGUI の色の入口が見つかりません。IMGUI 側は Unity のままになります。");
            }
        }

        public static void Restore()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            background = null;
        }

        private static void BackgroundPostfix(ref Color __result)
        {
            if (background.HasValue) __result = background.Value;
        }

        private static void TranslatePostfix(ref Color __result)
        {
            __result = IcebergTranslation.Translate(__result, translateDark);
        }
    }
}
#endif
