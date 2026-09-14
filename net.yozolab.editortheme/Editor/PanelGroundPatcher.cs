using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YozoLab.EditorTheme
{
    /// <summary>
    /// ウィンドウの「地」を塗り替える。
    ///
    /// 実機の診断で、テーマシートの色は解決されている（文字色は Iceberg になっている）のに
    /// 画面が灰色のままだった。ルート要素の背景は透明で、地色は UI の要素が描いているのではなく
    /// **パネルのクリア色**と、その下の **OS ウィンドウの背景色**で決まっている。
    ///
    ///   - <c>BaseVisualElementPanel.clearSettings</c>: 各ウィンドウの UI Toolkit パネルが
    ///     描画の最初にクリアする色。既定は透明で、下のネイティブの地色が透ける。
    ///     ここを不透明な Iceberg の色にすると、パネルが自分で地を塗る。
    ///   - <c>ContainerWindow.SetBackgroundColor</c>: OS ウィンドウ（ドックの隙間や枠の下）の
    ///     背景。ネイティブ側の呼び出しで、既定値は静的フィールド darkSkinColor / lightSkinColor。
    ///
    /// ウィンドウは後から開くので、数フレームおきに見回って新しいパネルにも当てる。
    /// 元の値は控えておき、無効化で戻す。
    /// </summary>
    internal static class PanelGroundPatcher
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        private static readonly Type ContainerWindowType = typeof(EditorGUIUtility).Assembly.GetType("UnityEditor.ContainerWindow");
        private static readonly MethodInfo SetBackgroundColor = ContainerWindowType?.GetMethod("SetBackgroundColor", Any, null, new[] { typeof(Color) }, null);
        private static readonly FieldInfo DarkSkinColor = ContainerWindowType?.GetField("darkSkinColor", Any);
        private static readonly FieldInfo LightSkinColor = ContainerWindowType?.GetField("lightSkinColor", Any);

        private static readonly Type GuiViewType = typeof(EditorGUIUtility).Assembly.GetType("UnityEditor.GUIView");
        private static readonly PropertyInfo ViewVisualTree = GuiViewType?.GetProperty("visualTree", Any);

        // パネル -> 元の clearSettings。パネルの型は internal なので object で持つ。
        private static readonly Dictionary<object, object> OriginalClear = new Dictionary<object, object>();
        private static PropertyInfo clearSettingsProperty;
        private static FieldInfo clearColorField;
        private static FieldInfo clearColorFlagField;

        private static Color? originalDarkSkin;
        private static Color? originalLightSkin;
        private static Color ground;
        private static Color windowGround;
        private static bool active;

        public static bool IsActive => active;
        public static int PatchedPanelCount => OriginalClear.Count;

        public static void Apply(Color panelGround, Color containerGround)
        {
            ground = panelGround;
            windowGround = containerGround;
            active = true;

            PatchContainerWindows(containerGround);
            Sweep();
        }

        /// <summary>まだ当てていないパネルに当てる。Tick から定期的に呼ぶ。</summary>
        public static void Sweep()
        {
            if (!active) return;

            foreach (IPanel panel in EnumeratePanels())
            {
                if (panel == null || OriginalClear.ContainsKey(panel)) continue;
                PatchPanel(panel, ground);
            }
        }

        public static void Restore()
        {
            active = false;

            foreach (KeyValuePair<object, object> pair in OriginalClear)
            {
                try { clearSettingsProperty?.SetValue(pair.Key, pair.Value); }
                catch (Exception) { /* 破棄済みのパネル */ }
            }
            OriginalClear.Clear();

            RestoreContainerWindows();
        }

        // ── パネル ───────────────────────────────────────────────────

        private static void PatchPanel(IPanel panel, Color color)
        {
            Type type = panel.GetType();
            clearSettingsProperty ??= type.GetProperty("clearSettings", Any);
            if (clearSettingsProperty == null || !clearSettingsProperty.CanWrite) return;

            object settings;
            try { settings = clearSettingsProperty.GetValue(panel); }
            catch (Exception) { return; }
            if (settings == null) return;

            Type settingsType = settings.GetType();
            clearColorField ??= settingsType.GetField("color", Any);
            clearColorFlagField ??= settingsType.GetField("clearColor", Any);
            if (clearColorField == null) return;

            // 戻すための控えは別の箱に入れる。GetValue が返す boxed 構造体をそのまま
            // 書き換えると、控えも同じ箱を指していて元の値が消える（最初そうなっていた）。
            object original = clearSettingsProperty.GetValue(panel);
            OriginalClear[panel] = original;

            clearColorField.SetValue(settings, color);
            clearColorFlagField?.SetValue(settings, true);
            clearSettingsProperty.SetValue(panel, settings);
        }

        private static IEnumerable<IPanel> EnumeratePanels()
        {
            var seen = new HashSet<IPanel>();

            if (GuiViewType != null && ViewVisualTree != null)
            {
                foreach (UnityEngine.Object view in Resources.FindObjectsOfTypeAll(GuiViewType))
                {
                    IPanel panel = null;
                    try { panel = (ViewVisualTree.GetValue(view) as VisualElement)?.panel; } catch (Exception) { }
                    if (panel != null && seen.Add(panel)) yield return panel;
                }
            }

            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                IPanel panel = null;
                try { panel = window.rootVisualElement?.panel; } catch (Exception) { }
                if (panel != null && seen.Add(panel)) yield return panel;
            }
        }

        // ── OS ウィンドウ ────────────────────────────────────────────

        private static void PatchContainerWindows(Color color)
        {
            if (ContainerWindowType == null) return;

            // 既定値を差し替えておくと、あとから開くウィンドウも最初からこの色になる。
            if (DarkSkinColor != null && originalDarkSkin == null)
            {
                originalDarkSkin = (Color)DarkSkinColor.GetValue(null);
                originalLightSkin = LightSkinColor != null ? (Color?)LightSkinColor.GetValue(null) : null;
                TrySetStatic(DarkSkinColor, color);
                TrySetStatic(LightSkinColor, color);
            }

            if (SetBackgroundColor == null) return;
            foreach (UnityEngine.Object window in Resources.FindObjectsOfTypeAll(ContainerWindowType))
            {
                try { SetBackgroundColor.Invoke(window, new object[] { color }); }
                catch (Exception) { /* 閉じかけのウィンドウ */ }
            }
        }

        private static void RestoreContainerWindows()
        {
            if (ContainerWindowType == null || originalDarkSkin == null) return;

            TrySetStatic(DarkSkinColor, originalDarkSkin.Value);
            if (originalLightSkin != null) TrySetStatic(LightSkinColor, originalLightSkin.Value);

            Color restored = EditorGUIUtility.isProSkin ? originalDarkSkin.Value : (originalLightSkin ?? originalDarkSkin.Value);
            if (SetBackgroundColor != null)
            {
                foreach (UnityEngine.Object window in Resources.FindObjectsOfTypeAll(ContainerWindowType))
                {
                    try { SetBackgroundColor.Invoke(window, new object[] { restored }); }
                    catch (Exception) { }
                }
            }

            originalDarkSkin = null;
            originalLightSkin = null;
        }

        /// <summary>static readonly でもリフレクションからは書ける。初期化済みの値の差し替えなので問題ない。</summary>
        private static void TrySetStatic(FieldInfo field, Color value)
        {
            if (field == null) return;
            try { field.SetValue(null, value); }
            catch (Exception) { }
        }
    }
}
