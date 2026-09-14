using UnityEditor;
using UnityEngine;

namespace YozoLab.EditorTheme
{
    /// <summary>設定ウィンドウ。有効・無効、配色、IMGUI の塗り替えの ON/OFF。</summary>
    internal sealed class EditorThemeWindow : EditorWindow
    {
        [MenuItem("YozoLab/Editor Theme")]
        private static void Open()
        {
            GetWindow<EditorThemeWindow>("Editor Theme").minSize = new Vector2(360f, 200f);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Iceberg Editor Theme", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "cocopon/iceberg.vim の配色を Unity エディタ全体に当てます。",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.ToggleLeft("テーマを適用する", EditorThemeApplier.Enabled);
            if (EditorGUI.EndChangeCheck()) EditorThemeApplier.SetEnabled(enabled);

            using (new EditorGUI.DisabledScope(!enabled))
            {
                EditorGUI.BeginChangeCheck();
                var variant = (ThemeVariant)EditorGUILayout.EnumPopup(
                    new GUIContent("配色", "Auto は Unity の Editor Theme（Dark / Light）に合わせます"),
                    EditorThemeApplier.Variant);
                if (EditorGUI.EndChangeCheck()) EditorThemeApplier.Variant = variant;

                EditorGUI.BeginChangeCheck();
                bool patch = EditorGUILayout.ToggleLeft(
                    new GUIContent("IMGUI の中身も塗り替える",
                        "Hierarchy / Project / Inspector など IMGUI で描かれる部分の文字色と選択色。"
                        + "崩れる箇所があれば切ってください（窓枠やタブの色はこれと無関係に変わります）"),
                    EditorThemeApplier.PatchImgui);
                if (EditorGUI.EndChangeCheck()) EditorThemeApplier.PatchImgui = patch;


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
    }
}
