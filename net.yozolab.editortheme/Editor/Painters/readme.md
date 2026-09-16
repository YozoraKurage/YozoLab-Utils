# Painters

Unity の内部に手を入れて、実際に色を塗る側。何をどこから塗るかは
[../readme.md](../readme.md) の「仕組み」に書いてある。

- `ThemeSheetRecolorer` … UI Toolkit のテーマシートの色テーブル
- `StyleCatalogRecolorer` … IMGUI の色の出所（StyleCatalog の色バッファ）
- `ImguiSkinPatcher` … GUISkin のテクスチャと文字色
- `StaticStylePatcher` … 型が抱え込んだ GUIStyle と Color
- `PanelGroundPatcher` … UI Toolkit パネルのクリア色と OS ウィンドウの地
- `ImguiBackgroundPatch` … Harmony で差し替える 2 つの色（VRCSDK がある環境のみ）
- `WindowsChromePatch` … Windows のタイトルバーとメニュー

どれも無効化で元に戻す。戻せないものは入れない。
