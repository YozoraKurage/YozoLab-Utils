# Theme

テーマそのもの。ここに Unity の内部を触る処理は無い。

- `ThemeRole` … 役割の一覧。テーマはこの役割ごとに色を 1 つ決める
- `ThemeTranslation` … 素の Unity の色 → 役割の表。Dark と Light で別
- `ThemeDefinition` … テーマ 1 つ分。JSON で読み書きできる
- `PresetThemes` … 組み込みの 12 テーマ
- `ThemeCatalog` … 組み込みと `Assets/YozoLabThemes/*.json` をまとめた一覧
- `ThemePalette` … IMGUI 側が使う 11 色。役割から導出する

テーマの増やし方は [../theme-authoring.md](../theme-authoring.md)。
