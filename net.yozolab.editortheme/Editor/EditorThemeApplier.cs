using UnityEditor;
using UnityEngine;

namespace YozoLab.EditorTheme
{
    /// <summary>どの配色を当てるか。</summary>
    internal enum ThemeVariant
    {
        /// <summary>Unity の Editor Theme（Dark / Light）に合わせる。</summary>
        Auto = 0,
        Dark = 1,
        Light = 2,
    }

    /// <summary>
    /// Iceberg の配色をエディタ全体に当てる入口。
    ///
    /// 実際の書き換えは 2 段に分かれる。
    ///   - UI Toolkit 側: <see cref="ThemeSheetRecolorer"/> がテーマシートの色テーブルを書き換える。
    ///     窓枠・タブ・ツールバー・UI Toolkit 製ウィンドウはこれで変わる。
    ///   - IMGUI 側: <see cref="ImguiSkinPatcher"/> が GUISkin を塗り替える。
    ///     Hierarchy / Project のツリーや Inspector の多くはこちら。
    ///
    /// 有効・無効と配色の選択は EditorPrefs に持つ。見た目の好みはユーザーのものなので、
    /// プロジェクトに混ぜて他の人の環境まで変えない。
    /// </summary>
    [InitializeOnLoad]
    public static class EditorThemeApplier
    {
        private const string PrefEnabled = "YozoLab.EditorTheme.Enabled";
        private const string PrefVariant = "YozoLab.EditorTheme.Variant";
        private const string PrefPatchImgui = "YozoLab.EditorTheme.PatchImgui";
        private const string PrefDebugPaint = "YozoLab.EditorTheme.DebugPaint";
        private const string PrefWindowsChrome = "YozoLab.EditorTheme.WindowsChrome";

        private const int ProSkinCheckIntervalFrames = 30;

        private static bool applied;
        private static bool appliedDark;
        private static int frameCounter;

        /// <summary>util-settings のトグル契約。</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefEnabled, true);
            private set => EditorPrefs.SetBool(PrefEnabled, value);
        }

        internal static ThemeVariant Variant
        {
            get => (ThemeVariant)EditorPrefs.GetInt(PrefVariant, (int)ThemeVariant.Auto);
            set { EditorPrefs.SetInt(PrefVariant, (int)value); Reapply(); }
        }

        /// <summary>IMGUI の GUISkin も塗り替えるか。見た目が合わないときに切れるよう別スイッチにしてある。</summary>
        internal static bool PatchImgui
        {
            get => EditorPrefs.GetBool(PrefPatchImgui, true);
            set { EditorPrefs.SetBool(PrefPatchImgui, value); Reapply(); }
        }

        /// <summary>
        /// Windows のタイトルバーとメニュー（右クリック含む）も暗くするか。
        /// OS の API を直接呼ぶだけで、同梱するバイナリは無い。切れば元に戻る。
        /// </summary>
        internal static bool WindowsChrome
        {
            get => EditorPrefs.GetBool(PrefWindowsChrome, true);
            set
            {
                EditorPrefs.SetBool(PrefWindowsChrome, value);
                if (!applied) return;
                if (value) WindowsChromePatch.Apply(appliedDark); else WindowsChromePatch.Restore();
            }
        }

        /// <summary>
        /// 診断用の原色モード。仕組みごとに全く違う色で塗り、どこを誰が塗っているかを見る。
        /// テーマとしては使い物にならないので既定は切ってある。
        /// </summary>
        internal static bool DebugPaint
        {
            get => EditorPrefs.GetBool(PrefDebugPaint, false);
            set { EditorPrefs.SetBool(PrefDebugPaint, value); Reapply(); }
        }

        /// <summary>今当たっているか（設定ではなく実状態）。</summary>
        internal static bool IsApplied => applied;

        /// <summary>この Unity で仕組みが成立しているか。</summary>
        internal static bool IsSupported => ThemeSheetRecolorer.IsSupported;

        /// <summary>IMGUI の地色（GetDefaultBackgroundColor）まで差し替えられる環境か。Harmony の有無。</summary>
        internal static bool CanPatchImguiBackground
        {
            get
            {
#if YOZOLAB_EDITORTHEME_HARMONY
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// 実機で何が起きているかを Console へ出す。効かないときの切り分け用。
        /// テーマシートの同一性、各ウィンドウのルートに付いているシート、
        /// ルートの解決済み背景色、IMGUI 地色の現在値を並べる。
        /// </summary>
        internal static void DumpDiagnostics()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[YozoLab Editor Theme] diagnostics: applied={applied} dark={appliedDark} isProSkin={EditorGUIUtility.isProSkin} "
                          + $"supported={IsSupported} harmony={CanPatchImguiBackground} patchedSheets={ThemeSheetRecolorer.PatchedCount}");

            System.Reflection.MethodInfo defaultBg = typeof(EditorGUIUtility).GetMethod("GetDefaultBackgroundColor",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (defaultBg != null)
            {
                sb.AppendLine($"  EditorGUIUtility.GetDefaultBackgroundColor() = #{ColorUtility.ToHtmlStringRGB((Color)defaultBg.Invoke(null, null))}");
            }

            sb.AppendLine(ThemeSheetRecolorer.DescribeSheets());
            sb.AppendLine($"  panels with Iceberg clear colour: {PanelGroundPatcher.PatchedPanelCount} (active={PanelGroundPatcher.IsActive})");
            sb.AppendLine($"  static GUIStyle copies patched: {StaticStylePatcher.PatchedCount}");
            sb.AppendLine($"  static Color fields patched: {StaticStylePatcher.PatchedColorCount}");
            sb.AppendLine($"  windows chrome: available={WindowsChromePatch.IsAvailable} applied={WindowsChromePatch.IsApplied} note={WindowsChromePatch.Note ?? "-"}");
            sb.AppendLine($"  style catalog colours patched: {StyleCatalogRecolorer.PatchedCount} error={StyleCatalogRecolorer.LastError ?? "-"}");
            sb.AppendLine($"  solid textures: {ImguiSkinPatcher.DescribeSolids()}");
            sb.AppendLine($"  chrome drift healed: {ImguiSkinPatcher.HealCount} times, last={ImguiSkinPatcher.LastDrift ?? "-"}");
            sb.AppendLine($"  prefs: variant={Variant} patchImgui={PatchImgui} debugPaint={DebugPaint}");

            // スキンの実状態。窓の地色は "dockarea"（ドック時）と "hostview"（浮遊時）の
            // テクスチャで決まる。ここが null なら何も描かれず、その下の色がそのまま見える。
            const System.Reflection.BindingFlags StaticAny = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            GUISkin defaultSkin = ImguiSkinPatcher.EditorDefaultSkin();
            var currentSkin = typeof(GUISkin).GetField("current", StaticAny)?.GetValue(null) as GUISkin;
            sb.AppendLine($"  GUISkin.current={(currentSkin ? currentSkin.name + "#" + currentSkin.GetInstanceID() : "null")} "
                          + $"default editor skin={(defaultSkin ? defaultSkin.name + "#" + defaultSkin.GetInstanceID() : "null")} "
                          + $"pixelsPerPoint={EditorGUIUtility.pixelsPerPoint}");
            foreach (EditorSkin kind in new[] { EditorSkin.Inspector, EditorSkin.Scene, EditorSkin.Game })
            {
                GUISkin skin = EditorGUIUtility.GetBuiltinSkin(kind);
                if (skin == null) { sb.AppendLine($"  skin {kind}: null"); continue; }
                sb.AppendLine($"  skin {kind}={skin.name}#{skin.GetInstanceID()}: hostview={DescribeBackground(skin.FindStyle("hostview"))} dockarea={DescribeBackground(skin.FindStyle("dockarea"))} "
                              + $"dragtab={DescribeBackground(skin.FindStyle("dragtab"))} TabWindowBackground={DescribeBackground(skin.FindStyle("TabWindowBackground"))}");
            }

            System.Type hostStyles = typeof(EditorGUIUtility).Assembly.GetType("UnityEditor.HostView+Styles");
            var hostBackground = hostStyles?.GetField("background", StaticAny)?.GetValue(null) as GUIStyle;
            sb.AppendLine($"  HostView.Styles.background: {(hostBackground == null ? "?" : "'" + hostBackground.name + "' bg=" + DescribeBackground(hostBackground))}");
            System.Type dockStyles = typeof(EditorGUIUtility).Assembly.GetType("UnityEditor.DockArea+Styles");
            var dockBackground = dockStyles?.GetField("background", StaticAny)?.GetValue(null) as GUIStyle;
            var dragTab = dockStyles?.GetField("dragTab", StaticAny)?.GetValue(null) as GUIStyle;
            sb.AppendLine($"  DockArea.Styles.background: {(dockBackground == null ? "?" : "'" + dockBackground.name + "' bg=" + DescribeBackground(dockBackground))} "
                          + $"dragTab: {(dragTab == null ? "?" : "'" + dragTab.name + "' bg=" + DescribeBackground(dragTab) + " on=" + (dragTab.onNormal.background ? dragTab.onNormal.background.name : "null"))}");

            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                UnityEngine.UIElements.VisualElement root;
                try { root = window.rootVisualElement; } catch (System.Exception) { continue; }
                if (root == null || root.panel == null) continue;

                var names = new System.Collections.Generic.List<string>();
                for (int i = 0; i < root.styleSheets.count; i++)
                {
                    UnityEngine.UIElements.StyleSheet sheet = root.styleSheets[i];
                    names.Add(sheet == null ? "null" : $"{sheet.name}#{sheet.GetInstanceID()}");
                }

                string clear = "?";
                try
                {
                    object settings = root.panel.GetType().GetProperty("clearSettings",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(root.panel);
                    clear = settings?.ToString() ?? "null";
                    var colorField = settings?.GetType().GetField("color");
                    var flagField = settings?.GetType().GetField("clearColor");
                    if (colorField != null) clear = "#" + ColorUtility.ToHtmlStringRGBA((Color)colorField.GetValue(settings))
                                                   + (flagField != null ? ((bool)flagField.GetValue(settings) ? "" : "(clearColor=false)") : "");
                }
                catch (System.Exception) { }

                sb.AppendLine($"  {window.GetType().Name}: children={root.childCount} sheets=[{string.Join(", ", names)}] "
                              + $"root bg=#{ColorUtility.ToHtmlStringRGBA(root.resolvedStyle.backgroundColor)} "
                              + $"color=#{ColorUtility.ToHtmlStringRGB(root.resolvedStyle.color)} panelClear={clear}");
            }

            Debug.Log(sb.ToString());
        }

        private static string DescribeBackground(GUIStyle style)
        {
            if (style == null) return "(no style)";
            Texture2D texture = style.normal.background;
            string one = texture ? $"{texture.name}#{texture.GetInstanceID()}" : "null";

            // 2 倍表示では描画側がこちらを使う。1 倍側だけ見ても当たったか分からない。
            Texture2D[] scaled = style.normal.scaledBackgrounds;
            if (scaled == null || scaled.Length == 0) return one + " scaled=(none)";
            return one + $" scaled[{scaled.Length}]={(scaled[0] ? scaled[0].name : "null")}";
        }

        static EditorThemeApplier()
        {
            // AssetDatabase とテーマの読み込みが整うまで待つ。
            EditorApplication.delayCall += () =>
            {
                if (Enabled) Apply();
            };
            EditorApplication.update += Tick;

            // ドメインが死ぬ前に必ず剥がす。GUISkin もテーマシートもパネルもネイティブ側の
            // 実体で、書き換えはドメインリロードをまたいで残る（実測: 単色テクスチャも
            // スキンの差し替えもリロード後に残っていた）。一方、戻すための控えは static なので
            // 消える。剥がさずにリロードすると、次のドメインは「差し替え済み」を原本として
            // 控え、二度と元に戻せない。剥がしておけば新しいドメインは常に素の状態から始まる。
            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                if (applied) RemoveForDomainReload();
            };
        }

        /// <summary>util-settings のトグル契約。</summary>
        public static void SetEnabled(bool value)
        {
            if (Enabled == value && applied == value) return;
            Enabled = value;
            if (value) Apply(); else Remove();
        }

        /// <summary>設定を変えたあとに一度剥がして当て直す。</summary>
        internal static void Reapply()
        {
            Remove();
            if (Enabled) Apply();
        }

        /// <summary>
        /// 定期処理。後から開いたウィンドウのパネルに地色を当てる。
        /// Editor Theme（Dark / Light）を切り替えられたら配色も追随させる。
        /// </summary>
        private static void Tick()
        {
            if (!applied) return;
            if (++frameCounter % ProSkinCheckIntervalFrames != 0) return;

            if (Variant == ThemeVariant.Auto && appliedDark != EditorGUIUtility.isProSkin)
            {
                Reapply();
                return;
            }

            PanelGroundPatcher.Sweep();

            // あとから開いたウィンドウが持ち込む自前のシートを拾う。
            ThemeSheetRecolorer.Sweep();

            // 当てたはずの窓枠スタイルが素に戻っていないか見張り、戻っていれば当て直す。
            if (PatchImgui) ImguiSkinPatcher.VerifyAndHeal();
        }

        private static void Apply()
        {
            if (!ThemeSheetRecolorer.IsSupported)
            {
                Debug.LogWarning("[YozoLab Editor Theme] この Unity ではテーマシートの内部構造が想定と違うため、色を変えられません。");
                return;
            }

            bool dark = Variant switch
            {
                ThemeVariant.Dark => true,
                ThemeVariant.Light => false,
                _ => EditorGUIUtility.isProSkin,
            };

            bool debug = DebugPaint;

            // 原色モードではシート由来の画素をピンクにする。翻訳表を通すと Iceberg の
            // 濃紺になってしまい、地の灰色と見分けが付きにくい。
            if (!ThemeSheetRecolorer.Apply(dark, debug ? new Color(1f, 0f, 0.66f, 1f) : (Color?)null))
            {
                Debug.LogWarning("[YozoLab Editor Theme] テーマシートを取得できませんでした。");
                return;
            }

            IcebergPalette.Palette palette = debug
                ? IcebergPalette.Debug
                : dark ? IcebergPalette.Dark : IcebergPalette.Light;

            // 窓の地色。パネルのクリア色と OS ウィンドウの背景の 2 段。
            // 原色モードでは、この 2 つも窓枠スタイルと見分けが付くよう別の色にする。
            PanelGroundPatcher.Apply(
                debug ? Color.blue : palette.Background,
                debug ? Color.white : palette.BackgroundDark);

            // IMGUI の色の出所。スキンのテクスチャより先に塗る。テクスチャを持たない
            // スタイルはここの色で描かれる（StylePainter がカタログを直接読む）。
            StyleCatalogRecolorer.Apply(dark);

            if (PatchImgui)
            {
                ImguiSkinPatcher.Apply(palette);
#if YOZOLAB_EDITORTHEME_HARMONY
                ImguiBackgroundPatch.Apply(palette.Background, dark);
#endif
            }

            applied = true;
            appliedDark = dark;

            if (WindowsChrome) WindowsChromePatch.Apply(dark);

#if YOZOLAB_EDITORTHEME_HARMONY
            PaintTracer.Apply();
#endif
        }

        private static void Remove()
        {
            ThemeSheetRecolorer.Restore();
            StyleCatalogRecolorer.Restore();
            PanelGroundPatcher.Restore();
            ImguiSkinPatcher.Restore();
            WindowsChromePatch.Restore();
#if YOZOLAB_EDITORTHEME_HARMONY
            ImguiBackgroundPatch.Restore();
#endif
            applied = false;
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        /// <summary>
        /// ドメインリロード直前用。コピーの修復は予約しない（コピーはドメインと一緒に消える）。
        /// 次のドメインの delayCall で素の状態から当て直される。
        /// </summary>
        private static void RemoveForDomainReload()
        {
            ThemeSheetRecolorer.Restore();
            StyleCatalogRecolorer.Restore();
            PanelGroundPatcher.Restore();
            ImguiSkinPatcher.RestoreForDomainReload();
#if YOZOLAB_EDITORTHEME_HARMONY
            ImguiBackgroundPatch.Restore();
#endif
            applied = false;
        }
    }
}
