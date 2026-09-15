using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YozoLab.EditorTheme
{
    /// <summary>設定ウィンドウ。テーマの選択と編集、塗り替えの ON/OFF。</summary>
    internal sealed class EditorThemeWindow : EditorWindow
    {
        // 編集中かどうかは専用のフラグで持つ。
        // [SerializeField] を付けた [Serializable] クラスは、ドメインリロード後に
        // null ではなく既定インスタンスになる（Unity の仕様）。null を「編集していない」の
        // 印に使うと、ウィンドウを開いただけで空の編集欄が出てしまう（実際に出た）。
        [SerializeField] private bool isEditing;
        [SerializeField] private ThemeDefinition editing;
        [SerializeField] private Vector2 scroll;

        /// <summary>色編集での並び。役割名の順ではなく、意味のまとまりで並べる。</summary>
        private static readonly (string group, ThemeRole[] roles)[] Groups =
        {
            ("面", new[] { ThemeRole.SurfaceDeepest, ThemeRole.Surface, ThemeRole.SurfaceRaised,
                           ThemeRole.SurfaceHover, ThemeRole.Selection }),
            ("部品", new[] { ThemeRole.Control, ThemeRole.ControlBright, ThemeRole.ControlBrightest,
                             ThemeRole.ControlSurface, ThemeRole.ControlSurfaceSelected }),
            ("文字", new[] { ThemeRole.TextFaint, ThemeRole.TextDim, ThemeRole.TextMuted,
                             ThemeRole.Text, ThemeRole.TextBright, ThemeRole.TextBrightest }),
            ("意味のある色", new[] { ThemeRole.Accent, ThemeRole.AccentBright, ThemeRole.Info,
                                     ThemeRole.InfoBright, ThemeRole.Error, ThemeRole.ErrorBright,
                                     ThemeRole.Warning, ThemeRole.WarningBright, ThemeRole.Purple,
                                     ThemeRole.Success }),
        };
        [MenuItem("YozoLab/Editor Theme")]
        private static void Open()
        {
            GetWindow<EditorThemeWindow>("Editor Theme").minSize = new Vector2(360f, 200f);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Editor Theme", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "エディタ全体の配色を差し替えます。テーマは JSON で追加できます（Editor/theme-authoring.md）。",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.ToggleLeft("テーマを適用する", EditorThemeApplier.Enabled);
            if (EditorGUI.EndChangeCheck()) EditorThemeApplier.SetEnabled(enabled);

            using (new EditorGUI.DisabledScope(!enabled))
            {
                DrawThemePicker();

                EditorGUI.BeginChangeCheck();
                bool patch = EditorGUILayout.ToggleLeft(
                    new GUIContent("IMGUI の中身も塗り替える",
                        "Hierarchy / Project / Inspector など IMGUI で描かれる部分の文字色と選択色。"
                        + "崩れる箇所があれば切ってください（窓枠やタブの色はこれと無関係に変わります）"),
                    EditorThemeApplier.PatchImgui);
                if (EditorGUI.EndChangeCheck()) EditorThemeApplier.PatchImgui = patch;


                using (new EditorGUI.DisabledScope(!WindowsChromePatch.IsAvailable))
                {
                    EditorGUI.BeginChangeCheck();
                    bool chrome = EditorGUILayout.ToggleLeft(
                        new GUIContent("Windows のタイトルバー・メニューも暗くする",
                            "OS が描くタイトルバーと右クリックメニューを暗くします。Windows の API を"
                            + "直接呼ぶだけで、同梱するバイナリはありません。切れば元に戻ります"),
                        EditorThemeApplier.WindowsChrome);
                    if (EditorGUI.EndChangeCheck()) EditorThemeApplier.WindowsChrome = chrome;
                }
                if (!WindowsChromePatch.IsAvailable)
                {
                    EditorGUILayout.LabelField("（Windows 以外では使えません）", EditorStyles.miniLabel);
                }

                EditorGUILayout.Space();
                EditorGUI.BeginChangeCheck();
                bool debug = EditorGUILayout.ToggleLeft(
                    new GUIContent("診断: 原色で塗り分ける",
                        "どの仕組みが画面のどこを塗っているかを見るための一時モードです。"
                        + "赤 hostview / 緑 dockarea / マゼンタ Toolbar / 黄 選択行 / "
                        + "シアン その他の GUISkin / 青 パネルのクリア色 / 白 OS ウィンドウ / "
                        + "ピンク テーマシート。テーマとしては使えません"),
                    EditorThemeApplier.DebugPaint);
                if (EditorGUI.EndChangeCheck()) EditorThemeApplier.DebugPaint = debug;

                if (EditorThemeApplier.DebugPaint)
                {
                    EditorGUILayout.HelpBox(
                        "原色モードです。赤 hostview、緑 dockarea・タブ、マゼンタ Toolbar、橙 ツールバー選択、"
                        + "黄 選択行、シアン その他の GUISkin のスタイル全部、青 UI Toolkit パネルのクリア色、"
                        + "白 OS ウィンドウの地色、ピンク テーマシート。"
                        + "灰色のまま残る場所は、このアドオンが触っていない何かが塗っています。"
                        + "確認が済んだら切ってください。",
                        MessageType.Warning);
                }

                EditorGUILayout.Space();
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("当て直す"))
                {
                    EditorThemeApplier.Reapply();
                }
                if (GUILayout.Button(new GUIContent("診断を Console へ",
                        "効かないときの切り分け用。テーマシートの状態と各ウィンドウの解決済み色を出します")))
                {
                    EditorThemeApplier.DumpDiagnostics();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                EditorThemeApplier.IsSupported
                    ? (EditorThemeApplier.IsApplied
                        ? (EditorThemeApplier.CanPatchImguiBackground ? "状態: 適用中" : "状態: 適用中（Harmony が無いため IMGUI の地色は Unity のまま）")
                        : "状態: 未適用")
                    : "状態: この Unity では未対応（内部構造が想定と違う）",
                EditorStyles.miniLabel);

            EditorGUILayout.HelpBox(
                "Unity 側の Editor Theme が Dark のときに最も自然に馴染みます（Preferences > General > Editor Theme）。"
                + "設定はユーザーごとに保存され、プロジェクトには入りません。",
                MessageType.Info);
        }

        /// <summary>テーマの選択と、複製して色を編集する導線。</summary>
        private void DrawThemePicker()
        {
            string[] names = ThemeCatalog.All.Select(t => t.name).ToArray();
            string[] options = new[] { "自動（明暗に合わせる）" }.Concat(names).ToArray();
            int current = string.IsNullOrEmpty(EditorThemeApplier.ThemeName)
                ? 0
                : System.Array.IndexOf(names, EditorThemeApplier.ThemeName) + 1;
            if (current < 0) current = 0;

            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup(
                new GUIContent("テーマ", "組み込みのテーマと、Assets/YozoLabThemes/*.json のテーマ"),
                current, options.Select(o => new GUIContent(o)).ToArray());
            if (EditorGUI.EndChangeCheck())
                EditorThemeApplier.ThemeName = picked == 0 ? string.Empty : names[picked - 1];

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("テーマを再読込", "Assets/YozoLabThemes/*.json を読み直します")))
            {
                ThemeCatalog.Reload();
                EditorThemeApplier.Reapply();
            }
            if (!isEditing && GUILayout.Button(new GUIContent("複製して色を編集",
                    "今のテーマを複製して、色を自由に決められます。組み込みのテーマは書き換えません")))
            {
                ThemeDefinition source = ThemeCatalog.Resolve(EditorGUIUtility.isProSkin);
                editing = source.Clone();
                editing.name = source.name + " のコピー";
                isEditing = true;
            }
            EditorGUILayout.EndHorizontal();

            if (isEditing && editing != null) DrawThemeEditor();
        }

        /// <summary>色の編集。役割ごとに 1 色決める。</summary>
        private void DrawThemeEditor()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                editing.name = EditorGUILayout.TextField(
                    new GUIContent("名前", "同じ名前のテーマがあれば上書きします"), editing.name);
                editing.isDark = EditorGUILayout.ToggleLeft(
                    new GUIContent("暗色テーマ", "Unity 側の Editor Theme が Dark のときに選ばれます"),
                    editing.isDark);

                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(280f));
                foreach ((string group, ThemeRole[] roles) in Groups)
                {
                    EditorGUILayout.LabelField(group, EditorStyles.miniBoldLabel);
                    foreach (ThemeRole role in roles)
                    {
                        Color before = editing.Get(role);
                        Color after = EditorGUILayout.ColorField(RoleLabel(role), before);
                        if (after != before) editing.Set(role, after);
                    }
                    EditorGUILayout.Space(2f);
                }
                EditorGUILayout.EndScrollView();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("保存して適用",
                        "Assets/YozoLabThemes/ に JSON として保存し、そのテーマを選びます")))
                {
                    if (string.IsNullOrWhiteSpace(editing.name))
                    {
                        Debug.LogWarning("[YozoLab Editor Theme] 名前が空です。");
                    }
                    else
                    {
                        string path = ThemeCatalog.Save(editing);
                        EditorThemeApplier.ThemeName = editing.name;
                        Debug.Log($"[YozoLab Editor Theme] 保存しました: {path}");
                        isEditing = false;
                    }
                }
                if (GUILayout.Button("編集をやめる")) isEditing = false;
                EditorGUILayout.EndHorizontal();
            }
        }

        private static GUIContent RoleLabel(ThemeRole role) => new GUIContent(role.ToString());
    }
}
