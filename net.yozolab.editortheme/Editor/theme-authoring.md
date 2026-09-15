# テーマの作り方

Editor Theme は配色を差し替えられます。テーマは **JSON 1 ファイル**で、C# を書く必要は
ありません。この文書は、利用者から「こんな配色にして」と言われた AI が、そのまま手順を
追えるように書いてあります。

## テーマの正体

テーマは「役割ごとの色」の集まりです。26 の役割に色を 1 つずつ決めるだけです。

Unity のテーマシートは生成済みで、規則の中の `var()` 参照が import 時に解決され、色が
リテラルで焼き込まれています。そのため「ボタンの背景」のような役割の区別は Unity 側には
残っていません。このアドオンは素の色から役割への表を内部に持っていて（`ThemeTranslation`）、
テーマ側は役割の色だけを決めれば済みます。

## 手順

1. **置き場所を作る。** `Assets/YozoLabThemes/`（プロジェクト内。パッケージは汚しません）
2. **JSON を書く。** 下の雛形の色を差し替える。26 の役割すべてに値を入れること
3. **保存する。** ファイル名は自由。`.json` であること
4. **反映する。** `YozoLab > Editor Theme` の `テーマを再読込` を押す
5. **選ぶ。** 同じ画面の `テーマ` から選ぶ

## 雛形

```json
{
  "name": "My Theme",
  "isDark": true,
  "accent": "#84a0c6",
  "accentBright": "#91acd1",
  "control": "#3d425b",
  "controlBright": "#444b71",
  "controlBrightest": "#5b6389",
  "error": "#e27878",
  "errorBright": "#e98989",
  "info": "#89b8c2",
  "infoBright": "#95c4ce",
  "purple": "#a093c7",
  "selection": "#2a3158",
  "success": "#b4be82",
  "surface": "#161821",
  "surfaceDeepest": "#0f1117",
  "surfaceHover": "#272c42",
  "surfaceRaised": "#1e2132",
  "text": "#c6c8d1",
  "textBright": "#d2d4de",
  "textBrightest": "#eff0f4",
  "textDim": "#818596",
  "textMuted": "#9a9ca5",
  "warning": "#e2a478",
  "warningBright": "#e9b189",
  "controlSurface": "#3d425b",
  "controlSurfaceSelected": "#5b6389",
  "textFaint": "#6b7089"
}
```

`name` は一覧に出る名前です。既存と同じ名前にすると上書き扱いになります。
`isDark` は暗色テーマかどうか。Unity 側の Editor Theme（Dark / Light）に応じて、
同じ明暗のテーマが選ばれます。**暗色と明色は別のテーマ**として作ってください。

## 役割の一覧

「使用数」は Unity の素の色のうち何色がその役割に割り当てられているかで、暗色 / 明色の順です。
0 のものは対応表では使われませんが、IMGUI 側で使うので値は必要です。

| フィールド | 何を塗るか | Iceberg Dark | Iceberg Light | 使用数 |
|---|---|---|---|---|
| `accent` | 強調・リンク・青系 | `#84a0c6` | `#2d539e` | 6 / 10 |
| `accentBright` | 強調の明るい側 | `#91acd1` | `#4f6ca8` | 1 / 0 |
| `control` | ボタンやドロップダウンの地 | `#3d425b` | `#757ca3` | 5 / 8 |
| `controlBright` | 枠線・スクロールのつまみ | `#444b71` | `#9fa7bd` | 7 / 14 |
| `controlBrightest` | 押下中・明るい枠線 | `#5b6389` | `#8b98b6` | 4 / 2 |
| `error` | エラー・赤系 | `#e27878` | `#cc517a` | 2 / 3 |
| `errorBright` | エラーの明るい側 | `#e98989` | `#d96f92` | 1 / 0 |
| `info` | 情報・シアン系 | `#89b8c2` | `#3f83a6` | 1 / 3 |
| `infoBright` | 情報の明るい側 | `#95c4ce` | `#5f9ab7` | 1 / 0 |
| `purple` | 紫系（マゼンタの置き換え） | `#a093c7` | `#7759b4` | 1 / 1 |
| `selection` | 選択行・選択タブ | `#2a3158` | `#a7b2cd` | 6 / 3 |
| `success` | 成功・緑系 | `#b4be82` | `#668e3d` | 1 / 1 |
| `surface` | 窓の地。いちばん広い面 | `#161821` | `#e8e9ec` | 4 / 3 |
| `surfaceDeepest` | 最も暗い面。ドックの隙間、入力欄の凹み、リストの地 | `#0f1117` | `#cad0de` | 16 / 14 |
| `surfaceHover` | ホバーと薄い強調 | `#272c42` | `#c9cdd7` | 8 / 5 |
| `surfaceRaised` | 一段持ち上がった面。ツールバー、見出しの帯 | `#1e2132` | `#dcdfe7` | 7 / 9 |
| `text` | 本文 | `#c6c8d1` | `#33374c` | 3 / 8 |
| `textBright` | 強調された文字 | `#d2d4de` | `#33374c` | 5 / 0 |
| `textBrightest` | 最も明るい文字（白の置き換え） | `#eff0f4` | `#e8e9ec` | 1 / 1 |
| `textDim` | 薄い文字 | `#818596` | `#8389a3` | 1 / 2 |
| `textMuted` | やや薄い文字 | `#9a9ca5` | `#6b7089` | 2 / 0 |
| `warning` | 警告・橙系 | `#e2a478` | `#c57339` | 2 / 3 |
| `warningBright` | 警告の明るい側 | `#e9b189` | `#d18a55` | 1 / 0 |
| `controlSurface` | IMGUI のメニューの地 | `#3d425b` | `#cad0de` | 0 / 0 |
| `controlSurfaceSelected` | IMGUI のメニューの選択中 | `#5b6389` | `#a7b2cd` | 0 / 0 |
| `textFaint` | 最も薄い文字（無効・補足） | `#6b7089` | `#8389a3` | 0 / 0 |

## 作るときの勘どころ

- **面の 4 段**（`surfaceDeepest` → `surface` → `surfaceRaised` → `surfaceHover`）は
  明るさの順序を守ってください。逆転すると奥行きが壊れます
- **文字の階調**（`textFaint` → `textDim` → `textMuted` → `text` → `textBright` →
  `textBrightest`）も順序を守ること。面との明度差を確保しないと読めなくなります
- **意味のある色**（`error` 赤 / `warning` 橙 / `success` 緑 / `info` 水色）は色相を
  大きく変えないでください。エラーが緑に見えると事故が起きます
- 値は `#rrggbb` の 6 桁。`#` は省略できます
- 埋め忘れた役割は**マゼンタ**で表示されます。気付けるようにわざとそうしてあります

## 確かめ方

`YozoLab > Editor Theme` で選んだあと、うまくいかない場合は同じ画面の
`診断を Console へ` を押すと、どの仕組みがどの色を持っているかが出ます。

`診断: 原色で塗り分ける` は、仕組みごとに原色を当てて「どこを誰が塗っているか」を見る
モードです。テーマの色が出ない場所を切り分けるときに使います。

## 内部の話

- 素の色から役割への表は `ThemeTranslation.cs`。Dark と Light で別の表です。同じ素の色でも
  役割が違うためで、たとえば `#101010` は暗色では面、明色では文字になります
- 表に無い灰色は、明るさの最も近い項目の役割へ寄せます。表に無い**色付きの色は触りません**。
  意味を持っている可能性があるためです
- テーマの役割から、IMGUI が使う 11 色（`IcebergPalette.Palette`）が導出されます
