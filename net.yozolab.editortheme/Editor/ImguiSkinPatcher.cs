using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace YozoLab.EditorTheme
{
    /// <summary>
    /// IMGUI 側の塗り替え。GUISkin の各スタイルが持つ文字色と、選択・見出しの
    /// 背景テクスチャを Iceberg の色に差し替える。
    ///
    /// USS が効くのは UI Toolkit で描かれる部分だけで、Hierarchy / Project のツリーや
    /// Inspector の大半は IMGUI が GUISkin から色を取って描いている。
    ///
    /// 文字色は「無彩色の灰色」だけを明るさに応じて Iceberg の前景色へ写す。
    /// 色付きの文字（警告の黄、リンクの青など）は意味を持っているので触らない。
    /// 背景は名前で狙い撃ちにする。テクスチャは角丸やグラデーションを持つものが多く、
    /// 一律に単色へ置き換えると崩れるため、選択行と見出しの帯だけにしている。
    ///
    /// 元の値は控えておき、無効化で戻す。GUISkin はドメインリロードで読み直されるので、
    /// 控えはドメインの中でだけ有効でよい。
    /// </summary>
    internal static class ImguiSkinPatcher
    {
        private readonly struct StateBackup
        {
            public readonly GUIStyleState State;
            public readonly Color TextColor;
            public readonly Texture2D Background;
            public readonly Texture2D[] ScaledBackgrounds;

            public StateBackup(GUIStyleState state)
            {
                State = state;
                TextColor = state.textColor;
                Background = state.background;
                ScaledBackgrounds = state.scaledBackgrounds;
            }

            public void Restore()
            {
                State.textColor = TextColor;
                State.background = Background;
                State.scaledBackgrounds = ScaledBackgrounds;
            }
        }

        private static readonly List<StateBackup> Backups = new List<StateBackup>();

        // 単色テクスチャは色ごとに 1 枚だけ作って使い回し、破棄しない。
        // 破棄すると、あとから初期化された型が差し替え済みスキンから作ったコピーに
        // 破棄済みの参照が残り、そのスタイルが描けなくなる（実測）。1x1 が数枚残るだけなので
        // 持ち続ける方が安全。
        private static readonly Dictionary<(Color color, int width, int height), Texture2D> SolidTextures =
            new Dictionary<(Color, int, int), Texture2D>();
        private static readonly Dictionary<Texture2D, Color> SolidColors = new Dictionary<Texture2D, Color>();

        private static readonly HashSet<Texture2D> OwnedTextures = new HashSet<Texture2D>();

        private static readonly EditorSkin[] Skins = { EditorSkin.Inspector, EditorSkin.Scene, EditorSkin.Game };

        private static readonly System.Reflection.MethodInfo GetDefaultSkin = typeof(GUIUtility).GetMethod("GetDefaultSkin",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(int) }, null);

        /// <summary>
        /// エディタのウィンドウが実際に使っているスキン。HostView は ResetGUIState で
        /// GUIUtility.GetDefaultSkin(1) を current にする。
        ///
        /// EditorSkin.Inspector が返すのは実測では LightSkin、EditorSkin.Scene が DarkSkin で、
        /// 名前から想像する対応とは違う。なので「既定のエディタスキン」はこの経路で取る。
        /// </summary>
        internal static GUISkin EditorDefaultSkin()
        {
            try
            {
                if (GetDefaultSkin?.Invoke(null, new object[] { 1 }) is GUISkin skin && skin.FindStyle("hostview") != null) return skin;
            }
            catch (Exception) { }

            string wanted = EditorGUIUtility.isProSkin ? "DarkSkin" : "LightSkin";
            GUISkin fallback = null;
            foreach (EditorSkin kind in Skins)
            {
                GUISkin skin = EditorGUIUtility.GetBuiltinSkin(kind);
                if (skin == null || skin.FindStyle("hostview") == null) continue;
                if (skin.name == wanted) return skin;
                fallback ??= skin;
            }
            return fallback;
        }

        /// <summary>差し替え対象のスキン（重複除去済み）。既定のエディタスキンを先頭に。</summary>
        private static IEnumerable<GUISkin> TargetSkins()
        {
            var seen = new HashSet<GUISkin>();
            GUISkin primary = EditorDefaultSkin();
            if (primary != null && seen.Add(primary)) yield return primary;
            foreach (EditorSkin kind in Skins)
            {
                GUISkin skin = EditorGUIUtility.GetBuiltinSkin(kind);
                if (skin != null && seen.Add(skin)) yield return skin;
            }
        }

        // 背景を差し替えるスタイル名の手掛かり。部分一致（大文字小文字は無視）。
        private static readonly string[] SelectionStyleHints = { "selection", "selected" };
        private static readonly string[] HeaderStyleHints =
        {
            "ol title", "projectbrowserheaderbg", "projectbrowserbottombarbg", "in title", "rl header",
        };

        /// <summary>
        /// 窓枠そのものを作っているスタイル。名前は完全一致（大文字小文字は無視）。
        ///
        /// 実機の DarkSkin を吐き出して確かめた: hostview の背景 'window back'、dockarea の
        /// 'dockarea back'、選択タブの 'tabbar on'、ツールバーの 'toolbar back' / 'Toolbar'。
        /// どれも数ピクセルの小さなテクスチャで、これが IMGUI の窓枠の地色になっている。
        /// 単色に置き換えると角丸や影は消えるが、テーマとしてはそれで足りる。
        /// </summary>
        private static readonly (string name, Surface surface)[] ChromeStyles =
        {
            ("hostview", Surface.Window),
            ("TabWindowBackground", Surface.Window),
            ("dockarea", Surface.Chrome),
            ("dockareaoverlay", Surface.Chrome),
            ("dragtab", Surface.Chrome),
            ("dragtab first", Surface.Chrome),
            ("dragtab scroller prev", Surface.Chrome),
            ("dragtab scroller next", Surface.Chrome),
            ("dockHeader", Surface.Chrome),
            ("AppToolbar", Surface.Chrome),
            ("Toolbar", Surface.Toolbar),
            ("toolbarbutton", Surface.Toolbar),
            ("ToolbarButton", Surface.Toolbar),
            ("ToolbarPopup", Surface.Toolbar),
            ("ToolbarDropDown", Surface.Toolbar),
            ("IN BigTitle", Surface.Toolbar),
            ("In BigTitle", Surface.Toolbar),
            ("IN BigTitle inner", Surface.Toolbar),
            // 描画を追いかけて見つけた、中身を覆っているのに素では背景が空のもの。
            ("ScrollViewAlt", Surface.Window),
            ("ProjectBrowserIconAreaBg", Surface.Window),
            ("OL box", Surface.Window),
            ("OL Box", Surface.Window),
            ("OL box flat", Surface.Window),
            ("OL box noexpand", Surface.Window),
            ("CN Box", Surface.Window),
            ("HelpBox", Surface.Toolbar),
            ("Box", Surface.Toolbar),
            ("RL Background", Surface.Window),
            ("window", Surface.Toolbar),
        };

        private enum Surface { Window, Chrome, Toolbar }

        private static bool applied;

        public static void Apply(IcebergPalette.Palette palette)
        {
            if (applied) Restore();

            // テクスチャではなく色を配る。実際の大きさはスタイルごとに決まる（SolidFor）。
            Color selection = palette.Selection;
            Color header = palette.Line;
            Color window = palette.Background;
            Color chrome = palette.BackgroundDark;
            Color chromeOn = palette.Background;
            Color toolbar = palette.Line;
            Color toolbarOn = palette.Menu;

            foreach (GUISkin skin in TargetSkins())
            {
                foreach (GUIStyle style in EnumerateStyles(skin))
                {
                    PatchStyle(style, palette, selection, header);
                }

                foreach ((string name, Surface surface) in ChromeStyles)
                {
                    GUIStyle style = ResolveChromeStyle(skin, name);
                    if (style == null) continue;
                    PatchChromeStyle(style, ChromeOff(surface, palette), ChromeOn(surface, palette));
                }
            }

            applied = true;
            healingDisabled = false;
            currentPalette = palette;
            currentColors = (selection, header, window, chrome, chromeOn, toolbar, toolbarOn);

            // エディタクラスが抱えているコピーは、GUI の文脈の中で当てる（下記）。
            ScheduleStaticCopies();
        }

        // ── static コピーへの適用と修復 ────────────────────────────────
        //
        // HostView の Styles.background のようなコピーは、型初期化子が GUISkin.current から
        // スタイルを引いて作る。まだ初期化されていない型を GUI の外で触ると、current が
        // 整っていないせいで空のコピーが作られ、以後ずっと残る（batchmode で実際に起きた）。
        //
        // なので、コピーに触る処理は全て、実際の IMGUI 描画の最中（Hierarchy / Project /
        // SceneView の描画コールバック）に回す。適用も修復も同じ。呼び出しは 1 度で済むので、
        // 走ったら購読を外す。テストからは、GUISkin.current を整えたうえで直接呼んでよい。

        private static IcebergPalette.Palette currentPalette;
        private static (Color selection, Color header, Color window, Color chrome, Color chromeOn, Color toolbar, Color toolbarOn) currentColors;
        private static bool applyPending;
        private static bool repairPending;
        private static bool subscribed;

        private static void ScheduleStaticCopies() { applyPending = true; Subscribe(); }
        private static void ScheduleRepair() { repairPending = true; Subscribe(); }

        private static void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGui;
            EditorApplication.projectWindowItemOnGUI += OnProjectItemGui;
            SceneView.duringSceneGui += OnSceneGui;
            InternalEditorUtility.RepaintAllViews();
        }

        private static void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed = false;
            EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyItemGui;
            EditorApplication.projectWindowItemOnGUI -= OnProjectItemGui;
            SceneView.duringSceneGui -= OnSceneGui;
        }

        private static void OnHierarchyItemGui(int instanceId, Rect rect) => RunPendingInGui();
        private static void OnProjectItemGui(string guid, Rect rect) => RunPendingInGui();
        private static void OnSceneGui(SceneView view) => RunPendingInGui();

        /// <summary>GUI の中で、溜まっている処理を順に片付ける。修復 → 適用の順。</summary>
        internal static void RunPendingInGui()
        {
            Unsubscribe();
            if (repairPending) { repairPending = false; RepairCopiesFromSkins(); }
            if (applyPending) { applyPending = false; ApplyStaticCopiesNow(); }
        }

        /// <summary>
        /// EditorStyles が抱えている GUIStyle。
        ///
        /// EditorStyles はスキンから引いたスタイルを <c>s_CachedStyles</c> に一度だけ抱え込み、
        /// 以後は作り直しません。こちらの差し替えより前に作られていると、
        /// <c>EditorStyles.inspectorTitlebar</c> などは塗る前の実体を指したままになり、
        /// 画面には Unity 既定の灰色が出続けます（原色モードで、Inspector の component 見出しだけ
        /// 原色にならず灰色のまま残ったのがこれ）。
        ///
        /// <c>s_CachedStyles</c> を空にして <c>SkinChanged()</c> を呼べば作り直せますが、
        /// それはやりません。レイアウトの途中でスタイルの寸法が変わり、IMGUI が
        /// "Getting control 0's position in a group with only 0 controls" で壊れます（実測）。
        /// 作り直させず、抱えている実体をそのまま塗ります。
        /// </summary>
        private static IEnumerable<GUIStyle> EnumerateEditorStyles()
        {
            var holders = new List<object>();
            try
            {
                Type type = typeof(EditorStyles);
                object current = type.GetField("s_Current", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
                if (current != null) holders.Add(current);

                if (type.GetField("s_CachedStyles", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) is Array cached)
                {
                    foreach (object entry in cached)
                    {
                        if (entry != null && !holders.Contains(entry)) holders.Add(entry);
                    }
                }
            }
            catch (Exception) { yield break; }

            foreach (object holder in holders)
            {
                FieldInfo[] fields;
                try { fields = holder.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public); }
                catch (Exception) { continue; }

                foreach (FieldInfo field in fields)
                {
                    if (field.FieldType != typeof(GUIStyle)) continue;
                    GUIStyle style = null;
                    try { style = field.GetValue(holder) as GUIStyle; }
                    catch (Exception) { }
                    if (style != null) yield return style;
                }
            }
        }

        /// <summary>コピーへ実際に当てる。GUI の文脈から呼ぶこと。</summary>
        internal static void ApplyStaticCopiesNow()
        {
            if (!applied || currentPalette == null) return;

            (Color selection, Color header, Color window, Color chrome, Color chromeOn, Color toolbar, Color toolbarOn) = currentColors;

            // 型が抱え込んでいる色も塗る。カタログを塗る前に初期化された静的フィールドは
            // 素の色のまま残っているため（Hierarchy の可視性列など）。
            StaticStylePatcher.PatchStaticColors(currentPalette.IsDark);

            foreach (GUIStyle style in StaticStylePatcher.EnumerateStyles().Concat(EnumerateEditorStyles()))
            {
                PatchStyle(style, currentPalette, selection, header);

                Surface? surface = ChromeSurfaceFor(style.name);
                if (surface.HasValue)
                {
                    PatchChromeStyle(style, ChromeOff(surface.Value, currentPalette), ChromeOn(surface.Value, currentPalette));
                }
            }

            InternalEditorUtility.RepaintAllViews();
        }

        public static void Restore()
        {
            if (!RestoreBackups()) return;

            // パッチの最中や後に初期化された型のコピーは、差し替え済みのスキンから作られて
            // いるので、控えでは戻らない（控え自体が単色テクスチャを指している）。
            // スキンは今戻ったので、次の GUI 描画で名前を引き直して本物の背景に付け替える。
            // （ここで直接やると、GUI 外で型初期化が走って壊れたコピーができる。）
            ScheduleRepair();
        }

        /// <summary>
        /// ドメインリロード直前用。スキンだけ戻し、コピーの修復は予約しない
        /// （コピーは static なのでドメインと一緒に消える）。GUI の外から呼んでよい。
        /// </summary>
        public static void RestoreForDomainReload()
        {
            RestoreBackups();
            Unsubscribe();
            repairPending = false;
        }

        private static bool RestoreBackups()
        {
            applyPending = false;
            currentPalette = null;
            if (!applied) return false;

            // 後から控えたものから戻す。同じ状態を二度控えていても最初の値で終わる。
            for (int i = Backups.Count - 1; i >= 0; i--) Backups[i].Restore();
            Backups.Clear();
            StaticStylePatcher.RestoreStaticColors();
            applied = false;
            return true;
        }


        private static GUIStyle ResolveChromeStyle(GUISkin skin, string name)
            => name == "window" ? skin.window : name == "Box" ? skin.box : skin.FindStyle(name);

        // ── ずれの見張りと直し ──────────────────────────────────────
        //
        // 一度当てたはずの窓枠スタイルが、あとから素に戻ったり null になったりする
        // （実機の診断で、DarkSkin の hostview だけ null になっていた。原因は特定できていない）。
        // GUISkin はエディタのリソースファイル上の実体で、こちらの与り知らないところで
        // 作り直されうる。当たっているかを定期的に見て、ずれていれば当て直す。

        /// <summary>当て直した回数。診断用。</summary>
        internal static int HealCount { get; private set; }

        /// <summary>最後にずれていた場所。診断用。</summary>
        internal static string LastDrift { get; private set; }

        /// <summary>直せないと分かったら見張りを降りる。当て直し（Apply）で解除。</summary>
        private static bool healingDisabled;

        /// <summary>
        /// 窓枠スタイルが期待どおりかを見て、ずれていれば当て直す。Tick から呼ぶ。
        /// 控えは最初の Apply で取ってあるので、ここでは控えを増やさない（増やすと
        /// 差し替え済みの値を「元の値」として覚えてしまう）。
        /// </summary>
        internal static bool VerifyAndHeal()
        {
            if (!applied || currentPalette == null || healingDisabled) return false;

            string drift = null;
            bool stuck = false;
            foreach (GUISkin skin in TargetSkins())
            {
                foreach ((string name, Surface surface) in ChromeStyles)
                {
                    GUIStyle style = ResolveChromeStyle(skin, name);
                    if (style == null || !MustHaveBackground(style.name)) continue;

                    Color expected = ChromeOff(surface, currentPalette);
                    if (HasBackground(style.normal, expected)) continue;

                    drift ??= $"{skin.name}.{name} ({(style.normal.background ? style.normal.background.name : "null")})";
                    SetBackground(style.normal, expected, style);

                    // 代入が効かない相手だと、毎回「ずれている」と見えて再描画を頼み続けて
                    // しまう。効いたかをその場で確かめ、効かないなら見張りを諦める。
                    if (!HasBackground(style.normal, expected)) stuck = true;
                }
            }

            if (drift == null) return false;

            HealCount++;
            LastDrift = drift + (stuck ? " ※代入が効かない" : string.Empty);

            if (stuck)
            {
                // 直せないものを毎フレーム叩いても画面が重くなるだけなので降りる。
                healingDisabled = true;
                return false;
            }

            // コピー側も作り直されている見込みなので、次の描画で当て直す。
            ScheduleStaticCopies();
            return true;
        }

        /// <summary>作った単色テクスチャの一覧。診断用。</summary>
        internal static string DescribeSolids()
        {
            var parts = new List<string>();
            foreach (KeyValuePair<(Color color, int width, int height), Texture2D> pair in SolidTextures)
            {
                Texture2D t = pair.Value;
                string actual;
                try { actual = t ? "#" + ColorUtility.ToHtmlStringRGB(t.GetPixel(0, 0)) : "destroyed"; }
                catch (Exception) { actual = "unreadable"; }
                parts.Add($"#{ColorUtility.ToHtmlStringRGB(pair.Key.color)} {pair.Key.width}x{pair.Key.height} got {actual}");
            }
            return parts.Count == 0 ? "(none)" : string.Join(", ", parts);
        }

        /// <summary>
        /// 窓の地の役割ごとの色。部品（ボタン・入力欄など）はここでは扱わない。
        /// あちらは StyleCatalogRecolorer が色の出所を塗り替えることで直る。
        /// </summary>
        private static Color ChromeOff(Surface surface, IcebergPalette.Palette p)
        {
            switch (surface)
            {
                case Surface.Chrome:  return p.BackgroundDark;
                case Surface.Toolbar: return p.Line;
                default:              return p.Background;
            }
        }

        private static Color ChromeOn(Surface surface, IcebergPalette.Palette p)
        {
            switch (surface)
            {
                case Surface.Chrome:  return p.Background;
                case Surface.Toolbar: return p.Menu;
                default:              return p.Background;
            }
        }

        private static Surface? ChromeSurfaceFor(string styleName)
        {
            if (string.IsNullOrEmpty(styleName)) return null;
            foreach ((string name, Surface surface) in ChromeStyles)
            {
                if (string.Equals(name, styleName, StringComparison.OrdinalIgnoreCase)) return surface;
            }
            return null;
        }

        /// <summary>
        /// 窓枠スタイルの背景を丸ごと単色にする。テクスチャが無い状態（描かない）は
        /// そのまま残す。off 系は通常、on 系は選択中（選択タブ・押されたボタン）。
        /// </summary>
        private static void PatchChromeStyle(GUIStyle style, Color off, Color on)
        {
            bool mustHave = MustHaveBackground(style.name);
            PatchChromeState(style.normal, off, mustHave, style);
            PatchChromeState(style.hover, off, false, style);
            PatchChromeState(style.active, off, false, style);
            PatchChromeState(style.focused, off, false, style);
            PatchChromeState(style.onNormal, on, mustHave && style.name.StartsWith("dragtab", StringComparison.OrdinalIgnoreCase), style);
            PatchChromeState(style.onHover, on, false, style);
            PatchChromeState(style.onActive, on, false, style);
            PatchChromeState(style.onFocused, on, false, style);
        }

        /// <summary>
        /// 素の状態で必ず背景テクスチャを持つ窓枠スタイル。ここが null なのは壊れている状態
        /// （実機の診断で hostview のコピーがそうなっていた。原因は特定できていない）なので、
        /// null でも当てて画面を取り戻す。控えは null のまま持つので、無効化すれば元の
        /// （壊れた）状態に戻る。
        /// </summary>
        private static bool MustHaveBackground(string name)
        {
            switch (name)
            {
                // ウィンドウの中身を実際に覆っているのはこれ。素の状態では背景が null で
                // 何も描かず、下の地色が透けている。描画を追いかけて突き止めた:
                //   style 'TabWindowBackground' bg=null rect=0,19 227x319
                // ちょうど各ビューの中身の矩形。ここを塗らないと窓の中は変わらない。
                case "TabWindowBackground":
                case "ScrollViewAlt":
                case "ProjectBrowserIconAreaBg":
                case "hostview":
                case "dockarea":
                case "dragtab":
                case "dragtab first":
                case "dockHeader":
                    return true;
                default:
                    return false;
            }
        }

        private static void PatchChromeState(GUIStyleState state, Color color, bool evenIfNull, GUIStyle style)
        {
            if (state == null) return;
            if (state.background == null && !evenIfNull) return;
            Backups.Add(new StateBackup(state));
            SetBackground(state, color, style);
        }

        /// <summary>
        /// 背景テクスチャを差し替える。1 倍の <c>background</c> だけでは足りない。
        ///
        /// GUIStyleState は HiDPI 用に <c>scaledBackgrounds</c> を別に持っていて、画面が
        /// 2 倍なら描画側はそちらを使う。実機（2 倍表示）で、こちらの差し替えが全て
        /// 内部的には成功しているのに画面が Unity 既定の灰色のままだったのはこれが理由。
        /// 元の配列が空なら Unity はもともと <c>background</c> に落ちるので触らない。
        /// </summary>
        private static void SetBackground(GUIStyleState state, Color color, GUIStyle style)
        {
            state.background = SolidFor(state.background, style, color);

            Texture2D[] scaled = state.scaledBackgrounds;
            if (scaled == null || scaled.Length == 0) return;

            // 配列の中身をその場で書き換えても native 側へ伝わらないので、作って代入する。
            var replaced = new Texture2D[scaled.Length];
            for (int i = 0; i < replaced.Length; i++) replaced[i] = SolidFor(scaled[i], style, color);
            state.scaledBackgrounds = replaced;
        }

        /// <summary>
        /// そのスタイルに置ける大きさの単色テクスチャを返す。元があれば同じ寸法にする。
        ///
        /// 1x1 では駄目でした。9 スライス（<c>style.border</c>）を持つスタイルに 1x1 を置くと
        /// 左右・上下の枠を切り出せず、何も描かれません。ドックされた窓の地色を塗っている
        /// <c>dockarea</c> は border が 6,0,6,4 で、まさにこれに当たります。差し替えは全て
        /// 成功しているのに画面が Unity 既定の灰色のままだった理由がこれでした。
        /// 元が無いスタイルには、枠を切り出せる最小の大きさを作ります。
        /// </summary>
        private static Texture2D SolidFor(Texture2D original, GUIStyle style, Color color)
        {
            int width, height;
            if (original != null)
            {
                width = Mathf.Max(1, original.width);
                height = Mathf.Max(1, original.height);
            }
            else
            {
                RectOffset border = style?.border;
                width = Mathf.Max(1, (border?.left ?? 0) + (border?.right ?? 0) + 2);
                height = Mathf.Max(1, (border?.top ?? 0) + (border?.bottom ?? 0) + 2);
            }
            return SolidTexture(color, width, height);
        }

        /// <summary>この状態がこちらの単色（その色）を指しているか。1 倍と 2 倍の両方を見る。</summary>
        private static bool HasBackground(GUIStyleState state, Color color)
        {
            if (!IsOurSolid(state.background, color)) return false;

            Texture2D[] scaled = state.scaledBackgrounds;
            if (scaled == null || scaled.Length == 0) return true;
            foreach (Texture2D t in scaled)
            {
                if (!IsOurSolid(t, color)) return false;
            }
            return true;
        }

        private static bool IsOurSolid(Texture2D texture, Color color)
            => texture != null && SolidColors.TryGetValue(texture, out Color c) && c == color;

        private static void PatchStyle(GUIStyle style, IcebergPalette.Palette palette, Color selection, Color header)
        {
            string name = style.name?.ToLowerInvariant() ?? string.Empty;
            bool isSelection = Matches(name, SelectionStyleHints);
            bool isHeader = Matches(name, HeaderStyleHints);

            // 原色モードでは、狙い撃ちしていないスタイルの背景も残らず塗る。
            // そうしないと「狙いから漏れているスタイル」と「そもそも IMGUI が
            // 描いていない場所」が、どちらも灰色のままで見分けられない。
            bool debug = ReferenceEquals(palette, IcebergPalette.Debug);

            foreach (GUIStyleState state in EnumerateStates(style))
            {
                if (state == null) continue;

                var backup = new StateBackup(state);
                bool changed = false;

                if (TryMapText(state.textColor, palette, out Color mapped))
                {
                    state.textColor = mapped;
                    changed = true;
                }

                if (state.background != null)
                {
                    if (isSelection) { SetBackground(state, selection, style); changed = true; }
                    else if (isHeader) { SetBackground(state, header, style); changed = true; }
                    else if (debug) { SetBackground(state, palette.Visual, style); changed = true; }
                }

                if (changed) Backups.Add(backup);
            }
        }

        /// <summary>
        /// 無彩色の灰色だけを、明るさで 3 段に丸めて Iceberg の前景色へ。
        /// Light 配色では向きが逆（暗い文字が本文）になるので分けている。
        /// </summary>
        private static bool TryMapText(Color color, IcebergPalette.Palette palette, out Color mapped)
        {
            mapped = color;
            if (color.a <= 0f) return false;

            float max = Mathf.Max(color.r, color.g, color.b);
            float min = Mathf.Min(color.r, color.g, color.b);
            if (max - min > 0.06f) return false; // 色が付いている

            float luminance = (max + min) * 0.5f;

            if (palette.IsDark)
            {
                if (luminance >= 0.8f) mapped = palette.ForegroundBright;
                else if (luminance >= 0.55f) mapped = palette.Foreground;
                else if (luminance >= 0.3f) mapped = palette.ForegroundDim;
                else return false;
            }
            else
            {
                if (luminance <= 0.25f) mapped = palette.Foreground;
                else if (luminance <= 0.5f) mapped = palette.ForegroundDim;
                else return false;
            }

            mapped.a = color.a;
            return true;
        }

        private static bool Matches(string name, string[] hints)
        {
            foreach (string hint in hints)
            {
                if (name.Contains(hint)) return true;
            }
            return false;
        }

        private static IEnumerable<GUIStyle> EnumerateStyles(GUISkin skin)
        {
            yield return skin.box;
            yield return skin.button;
            yield return skin.toggle;
            yield return skin.label;
            yield return skin.textField;
            yield return skin.textArea;
            yield return skin.window;
            yield return skin.horizontalSlider;
            yield return skin.horizontalSliderThumb;
            yield return skin.verticalSlider;
            yield return skin.verticalSliderThumb;
            yield return skin.horizontalScrollbar;
            yield return skin.horizontalScrollbarThumb;
            yield return skin.horizontalScrollbarLeftButton;
            yield return skin.horizontalScrollbarRightButton;
            yield return skin.verticalScrollbar;
            yield return skin.verticalScrollbarThumb;
            yield return skin.verticalScrollbarUpButton;
            yield return skin.verticalScrollbarDownButton;
            yield return skin.scrollView;

            if (skin.customStyles == null) yield break;
            foreach (GUIStyle style in skin.customStyles)
            {
                if (style != null) yield return style;
            }
        }

        private static IEnumerable<GUIStyleState> EnumerateStates(GUIStyle style)
        {
            yield return style.normal;
            yield return style.hover;
            yield return style.active;
            yield return style.focused;
            yield return style.onNormal;
            yield return style.onHover;
            yield return style.onActive;
            yield return style.onFocused;
        }

        private static Texture2D SolidTexture(Color color, int width, int height)
        {
            (Color color, int width, int height) key = (color, width, height);
            if (SolidTextures.TryGetValue(key, out Texture2D existing) && existing != null) return existing;

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "YozoLab.EditorTheme.Solid",
            };

            var pixels = new Color32[width * height];
            var packed = (Color32)color;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = packed;
            texture.SetPixels32(pixels);
            texture.Apply();

            SolidTextures[key] = texture;
            SolidColors[texture] = color;
            OwnedTextures.Add(texture);
            return texture;
        }

        /// <summary>
        /// static コピーのうち、背景がこちらの単色テクスチャを指しているものを、
        /// 復元済みのスキンから同名スタイルを引いて本物へ戻す。GUI の文脈から呼ぶこと。
        /// </summary>
        internal static void RepairCopiesFromSkins()
        {
            var skins = new List<GUISkin>(TargetSkins());

            foreach (GUIStyle copy in StaticStylePatcher.EnumerateStyles())
            {
                if (string.IsNullOrEmpty(copy.name)) continue;

                GUIStyle original = null;
                foreach (GUISkin skin in skins)
                {
                    original = skin?.FindStyle(copy.name);
                    if (original != null) break;
                }
                if (original == null) continue;

                RepairState(copy.normal, original.normal);
                RepairState(copy.hover, original.hover);
                RepairState(copy.active, original.active);
                RepairState(copy.focused, original.focused);
                RepairState(copy.onNormal, original.onNormal);
                RepairState(copy.onHover, original.onHover);
                RepairState(copy.onActive, original.onActive);
                RepairState(copy.onFocused, original.onFocused);
            }
        }

        private static void RepairState(GUIStyleState copy, GUIStyleState original)
        {
            if (copy == null || original == null) return;
            if (copy.background != null && OwnedTextures.Contains(copy.background))
            {
                copy.background = original.background;
            }

            Texture2D[] scaled = copy.scaledBackgrounds;
            if (scaled != null && scaled.Length > 0 && scaled[0] != null && OwnedTextures.Contains(scaled[0]))
            {
                copy.scaledBackgrounds = original.scaledBackgrounds;
            }
        }
    }
}
