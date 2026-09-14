# Editor Theme (Iceberg)

Unity エディタ全体の配色を [Iceberg](https://github.com/cocopon/iceberg.vim)（cocopon 氏の
Vim カラースキーム）に差し替えるエディタ拡張です。Unity 標準の Dark テーマの上に被せる形で、
窓枠・タブ・ツールバー・各ウィンドウの背景と文字を Iceberg の青みがかった暗色に揃えます。

## 使い方

- `YozoLab > Editor Theme` を開くと ON/OFF と配色を切り替えられます。
- `配色` は `Auto` で Unity の Editor Theme（Dark / Light）に追随します。Iceberg にも
  light 配色があるので、Light テーマにはそちらが当たります。
- `IMGUI の中身も塗り替える` は Hierarchy / Project / Inspector の文字色と選択色の切り替えです。
  崩れる箇所があればここだけ切れます（窓枠やタブはこの設定と無関係に変わります）。

設定はユーザーごと（EditorPrefs）に保存され、プロジェクトには入りません。

## 仕組み

Unity 2022 のエディタ UI は、窓枠・タブ・ツールバー・UI Toolkit 製の各ウィンドウが
共通のテーマシート 1 枚から色を取っています。このツールはそのシートの**色テーブル
（449 色）をその場で Iceberg に翻訳して書き換えます**。シートは全ウィンドウで共有されて
いるので 1 回で全部に効き、あとから開いたウィンドウにも自動で効きます。

USS 変数（`--unity-colors-*`）の上書きではないのは、配布されているシートが生成済み
（`_inter`）で、規則の中の `var()` 参照が import 時に解決されて色がリテラルで
焼き込まれているためです。変数は定義だけ残っていて、Unity 自身の規則はもう参照して
いません。実機で測って分かったことで、変数を書き換えても何も変わりませんでした。

その代わり、同じ灰色を複数の役割（ボタンの背景とドロップダウンの hover など）が
共有しているため、翻訳は役割ごとではなく **色の値ごとの 1 対 1** になります。
`Editor/IcebergTranslation.cs` がその表で、左側は実際のテーマシートから吐き出した
固有色（Dark 93 色 / Light 97 色）です。

既に開いているウィンドウには、シートの contentHash を変え、計算済みスタイルの
キャッシュを捨て、各ルートの版を上げて追随させます（UI Toolkit は「マッチした規則の
組が同じなら計算し直さない」ので、テーブルを書き換えただけでは残ります）。

共通シート 1 枚では足りません。実機の診断で、ウィンドウによっては自前のシートを併せて
持っていることが分かりました（`InspectorWindow`、`SettingsWindowDark`、各アドオンのシートなど）。
共通シートしか塗り替えていなかったので、そういうウィンドウの中身は元の灰色のまま残ります。
今は各ウィンドウのルートに付いているシートも同じ翻訳表で塗り替え、あとから開いた
ウィンドウのぶんは定期処理で拾います。

**それでもまだ足りませんでした。** 読み込まれているシートを数えたところ、21 枚のうち
色を持つ 8 枚が塗り漏れていました。

```
not patched: OverlayCommon(6), OverlayDark(14), MainToolbarDark(6),
             ToolbarDark_inter.uss(307), SceneViewToolbarElements(1), EditorToolbarDark(6)
```

ツールバーやオーバーレイは自前のシートを要素の奥に付けており、ウィンドウのルートを
辿るだけでは届きません。`ToolbarDark_inter.uss` だけで 307 色あり、画面上部の帯が
まるごと素のままでした。今は `Resources.FindObjectsOfTypeAll<StyleSheet>()` で
読み込み済みのシートを直接さらいます。今の配色と逆のシート（Dark 適用中の Light 用）は
触りません。Unity の Editor Theme を切り替えたときに変な色で残るためです。

これで塗れるシートが 2 枚から 8 枚になり、素の灰色が 17.38% から 15.22% へ下がりました。

書き換えるのは色テーブルだけでアセットには保存されません。ただし**ドメインリロードでは
素に戻りません**。シートも GUISkin もパネルもネイティブ側の実体で、書き換えはリロードを
またいで残る一方、戻すための控え（static）は消えます。剥がさずにリロードすると次の
ドメインが「差し替え済み」を原本として控えてしまい、二度と戻せなくなるので、
`AssemblyReloadEvents.beforeAssemblyReload` で必ず全部剥がし、新しいドメインが素の状態
から当て直します。無効化では控えた元の値を戻します。

### 窓の地色

シートの色が解決されていても画面が変わらなかったのは、**窓の地色が UI の要素で描かれて
いない**ためです（ルート要素の背景は透明）。実際に地を塗っているのは IMGUI で、
ドックされた窓は `DockArea` がドック領域全体に `dockarea` スタイルのテクスチャを描き
（`DrawDockAreaBackground`）、浮いている窓は `HostView` が `hostview` のテクスチャで
塗ります（`TabWindowBackground` はテクスチャを持たず何も描きません。2022.3 の実測）。
これらは下の「IMGUI の色」で差し替えます。その下にはさらに 2 段あります。

- **各ウィンドウの UI Toolkit パネルのクリア色**（`clearSettings`）。既定は透明で、下の
  ネイティブの色が透けます。これを不透明な Iceberg にして、パネル自身に地を塗らせます。
  後から開いたウィンドウには定期的な見回りで当てます。
- **OS ウィンドウの背景**（`ContainerWindow.SetBackgroundColor`、ドックの隙間や枠の下）。
  ネイティブ側の呼び出しで、既定値は静的フィールド `darkSkinColor`。こちらも Iceberg にし、
  既定値も差し替えてあとから開くウィンドウに効かせます。

### 窓の中身を塗っているもの

**`TabWindowBackground` が本丸でした。** ここに辿り着くまで何度も外したので、経緯ごと残します。

`HostView.InvokeOnGUI` は、ビューの中身の矩形をこのスタイルで囲みます。

```csharp
BeginOffsetArea(m_ActualView.rootVisualElement.worldBound, GUIContent.none, Styles.tabWindowBackground);
```

素の `TabWindowBackground` は背景テクスチャが **null** で、何も描きません。だから下の地色が
そのまま見えていました。ここを塗らない限り、窓の中は何をしても変わりません。

同じ理由で `ScrollViewAlt`（リストの背景）と `ProjectBrowserIconAreaBg`（Project のアイコン領域）も
素では空です。この 3 つは背景として先に描かれるので、塗っても中身は上に乗ります。
逆に `dockareaOverlay` は最後に上から描かれるので、絶対に塗ってはいけません。

`scrollview`（`skin.scrollView` そのもの）は意図的に外しています。全てのスクロールビューに
不透明な背景が付いてしまううえ、`TabWindowBackground` が既に下を塗っているので要りません。

`dockarea` も塗っていますが、これだけでは中身は埋まりません。素のテクスチャが 10x19 で
border が 6,0,6,4、つまり左右の枠だけで幅を超えるため、9 スライスの中央が成立せず、
縁しか描かれないからです。タブ帯の色はこれで変わります。

### IMGUI の色

IMGUI で描かれるウィンドウ（Inspector、Hierarchy、Project、Preferences、Animator など、
2022.3 ではまだ大半）は UI Toolkit のシートを見ません。色の出どころは 3 つです。

- **`EditorResources.styleCatalog`** という IMGUI 専用の色表（100 色）。コードからは
  `SVC<Color>.value` を通して読まれます。読み出しのたびに翻訳表を通すよう **Harmony で
  Postfix** しているので、キャッシュの有無に関わらず Iceberg になります。
- **`EditorGUIUtility.GetDefaultBackgroundColor()`**（Dark で `#383838`）。同じく Harmony で
  戻り値を差し替えます。
- **GUISkin の窓枠テクスチャ**（`hostview` の `window back`、`dockarea back`、選択タブの
  `tabbar on`、`toolbar back` など）。Iceberg の単色テクスチャに差し替えます（角丸や影は
  失われますが、テーマとしてはそれで足ります）。

  **単色テクスチャは元と同じ寸法で作ります。** 当初は 1x1 を置いていましたが、9 スライス
  （`style.border`）を持つスタイルに 1x1 を置くのは筋が悪いやり方でした。ドックされた窓の
  地色を塗る `dockarea` は border が 6,0,6,4 で、幅 1 から左右 6 ずつは切り出せません。
  今は元のテクスチャと同じ寸法の単色を作って置き換え、元が無いスタイルには枠を切り出せる
  最小の大きさを作ります。

  ただしこれが「画面が変わらなかった原因」だとは言い切れません。Unity 自身、枠より小さい
  テクスチャを配っていることがあるからです（`AppToolbar` は横枠 6 に対しテクスチャ幅 4）。
  Unity 側で何らかの丸めが入っているはずで、1x1 が本当に描かれないのかは確かめていません。
  寸法を合わせるのは、素の見た目に忠実であるという理由で正しいというだけです。

  **1 倍の背景だけでは足りません。** GUIStyleState は HiDPI 用の背景を
  `scaledBackgrounds` に別途持っていて、画面が 2 倍なら描画側はそちらを使います。
  今は 1 倍と 2 倍の両方を差し替えます。元の配列が空のスタイルは、もともと Unity が
  1 倍側に落ちるので触りません。なお `pixelsPerPoint` が 1 の環境では 1 倍側しか
  使われないので、こちらは効いてきません（診断に `pixelsPerPoint` を出しています）。

  スキンの取り方に罠があります。`EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector)` が
  返すのは実測では **LightSkin**、`EditorSkin.Scene` が **DarkSkin** で、名前から想像する
  対応と違います。ウィンドウが実際に使う既定のスキンは `GUIUtility.GetDefaultSkin(1)`
  （internal）で、これを先頭に 3 つとも差し替えます。

  `hostview` / `dockarea` / `dragtab` / `dockHeader` の `normal` は素の状態で必ずテクスチャを
  持つので、null でも当てます。null なのは壊れている状態（実機の診断で `hostview` の
  コピーがそうなっていました。原因は特定できていません）で、「null は触らない」だと
  以後どの当て直しも素通りし、窓が何も描かれないまま固まるためです。
- **エディタクラスが抱えている GUIStyle の static コピー**。ここが最後まで見落としていた
  本丸で、`HostView.Styles.background = new GUIStyle("hostview")` のように、Unity のエディタ
  クラスはスキンからのコピーを static に持ち、それで窓全体を塗っています。スキンの原本を
  いくら書き換えてもコピーには届きません。UnityEditor 系アセンブリの入れ子型（`Styles` など）
  から GUIStyle 型の static フィールドを列挙し、同じ規則で差し替えます（約 200 個）。

  コピーに触る処理は、実際の IMGUI 描画の最中（Hierarchy / Project / SceneView の描画
  コールバック）にだけ走らせます。未初期化の型を GUI の外で触ると、`GUISkin.current` が
  整っていないせいで空のコピー（`StyleNotFoundError`）が作られてそのまま居座るためです
  （batchmode では current が GameSkin で、実際に起きた）。列挙のあいだは current が
  `hostview` を持つスキンであることを確かめ、違えば既定のエディタスキンに差し替えます。
  そのため有効化・無効化から反映まで、描画 1 回ぶんの遅れがあります。

  `EditorStyles`（`EditorStyles.label` など）は入れ子型ではないのでこの列挙には入りませんが、
  中身の 101 個のうち 95 個は `GetStyle("…")`、つまりスキンのスタイルそのものへの参照で、
  スキンを塗り替えれば一緒に変わります（実測）。残る 6 個は余白だけを変えたコピーで背景を
  持ちません。なので `EditorStyles` を別途たどる必要はありません。

- **当てたはずのスタイルが素に戻ることがある**。実機の診断で、DarkSkin の `hostview` だけが
  null に戻っていました（原因は特定できていません）。GUISkin はエディタのリソースファイル上の
  実体で、こちらの与り知らないところで作り直されえます。そこで定期処理（30 フレームごと）で
  窓枠スタイルが期待どおりかを見て、ずれていれば当て直します。直した回数と場所は診断に出ます。
  代入しても効かない相手だと分かった時点で見張りを降ります（毎フレーム叩いて重くしないため）。

Harmony を同梱する VRCSDK（`com.vrchat.base`）が無い環境では、上 2 つは Unity のままに
なります（設定ウィンドウにその旨が出ます）。

あわせて GUISkin の各スタイルが持つ文字色を明るさに応じて Iceberg の前景色へ写し、
選択行と見出しの帯の背景も単色に差し替えます。無効化ではすべて元へ戻します
（Harmony は Unpatch、クリア色・テクスチャ・文字色は控えた元の値）。

## 効かないとき

`YozoLab > Editor Theme` の `診断を Console へ` を押すと、テーマシートの同一性、
各ウィンドウのルートに付いているシートと解決済みの色、IMGUI 地色の現在値、3 つの
GUISkin の `hostview` / `dockarea` / `dragtab` の実テクスチャ、`HostView` と `DockArea` が
抱えているコピーの実状態が Console に出ます。窓の地色が変わらないなら、まず
`skin ...: dockarea=` と `DockArea.Styles.background` を見てください。`YozoLab.EditorTheme.Solid`
なら当たっていて、`null` ならスキンが壊れています（Unity を再起動すると素に戻ります）。

## 診断: 原色で塗り分ける

設定ウィンドウの `診断: 原色で塗り分ける` は、テーマの代わりに仕組みごとの原色を当てる
一時モードです。「内部的には全部当たっているのに画面が変わらない」が続いたときに、
推測をやめて塗り分けて見るために用意しました。スクリーンショットを 1 枚撮れば、
画面のどこを誰が塗っているのかが分かります。

| 色 | 塗っている仕組み |
|---|---|
| 赤 | `hostview` / `TabWindowBackground` / `OL box` |
| 緑 | `dockarea` / `dragtab` / `dockHeader` |
| マゼンタ | `Toolbar` / `HelpBox` / `IN BigTitle` |
| 橙 | ツールバーの選択状態 |
| 黄 | 選択行 |
| シアン | 上記以外の GUISkin のスタイル全部 |
| 青 | UI Toolkit パネルのクリア色 |
| 白 | OS ウィンドウの地色 |
| ピンク | テーマシート由来 |

灰色のまま残る場所があれば、そこはこのアドオンが触っていないどこかが塗っています。
テーマとしては使えないので、確認が済んだら切ってください。

### やってみて外したこと

部品（ボタン・入力欄・見出し）の地色を自動で合わせようと、元テクスチャを RenderTexture へ
焼いて平均色を取り、翻訳表に通して置き換える作りを一度入れました。**外しました。**
計測すると `sampled ok=0 failed=0 replaced=1` のままで、素の灰色も減りませんでした。
平均色を取る処理がなぜ走らないのかを説明できないまま、600 個近いスタイルに対して
GPU の読み戻しを回す作りを残すのは割に合わないと判断しています。

本当の原因は別でした（下記）。狙うスタイルを名前で挙げる必要もありませんでした。

### 色の出所は StyleCatalog

**ここが本丸でした。** IMGUI のスタイルは USS から作られた `StyleCatalog` を持ち、
あらゆるブロックの色が `buffers.colors` という `Color[]` 1 本への添字に解決されます。

```
StyleBlock.GetColor(key) -> catalog.buffers.colors[bufferIndex]
```

そして `GUIStyle.onDraw` には `StylePainter.DrawStyle` が入っていて、冒頭でこう分岐します。

```csharp
if (gs == GUIStyle.none || gs.blockId == -1 || String.IsNullOrEmpty(gs.name) || gs.normal.background != null)
    return false;              // テクスチャがあれば Unity の描画に返す
DrawBlock(gs, block, position, content, states);   // 無ければカタログの色で塗る
```

`DrawBlock` は `background-color` を都度カタログから読み、白テクスチャを色で染めて描きます。
角丸も枠もここで描かれます。

```csharp
var backgroundColor = block.GetColor(StyleCatalogKeyword.backgroundColor);
GUI.DrawTexture(drawRect, EditorGUIUtility.whiteTexture, ..., backgroundColor * bgColorTint, ..., border.radius, smoothCorners);
```

つまり**背景テクスチャを持たないスタイルは、テクスチャではなくこの表の色で塗られている**。
ボタンも入力欄もドロップダウンも見出しも `bg=null` なのに灰色で描かれていたのはこれが理由です。
スキンのテクスチャをいくら差し替えても届きません。

`StyleCatalogRecolorer` がこの配列を翻訳表に通して直接書き換えます。カタログの再構築は
起こしません（起こすと素の色を読み直してしまう）。`StylePainter` は都度読むので、
書き換えた瞬間から効きます。スタイルの組み立て直しは不要で、やると寸法が動いて
レイアウトが壊れるのでやりません。

実測では 100 色。素の灰色が 8.46% から **0.83%** へ落ちました。

### 型が抱え込んだ色

カタログを塗っても、**型初期化のときに読んだ色を static に抱え込んでいる箇所**は素のまま残ります。
スタイルのときと同じ問題です。たとえば Hierarchy の可視性列がこれでした。

```csharp
// SceneVisibilityHierarchyGUI.cs
public static readonly Color backgroundColor =
    EditorResources.GetStyle("game-object-tree-view-scene-visibility")...
using (new GUI.BackgroundColorScope(Styles.backgroundColor))
```

白テクスチャをこの色で染めて描くので、カタログを後から塗り替えても届きません。
個別に狙い撃ちせず、`StaticStylePatcher` が static な `Color` フィールドもまとめて
翻訳表に通します。色付きの値は表が素通しするので、警告の黄やエラーの赤は変わりません。

実測で 17 個。残っていた素の灰色が 0.83% から **0.11%** になり、閾値以上の灰色は無くなりました。

**部品にテクスチャを差し込む方法は撤回しました。** 上の分岐のとおり、テクスチャを入れると
`DrawStyle` が降りてしまい、角丸も枠も状態ごとの描き分けも失われます。仕組みと戦う形でした。
出所を塗れば、Unity 本来の描画のまま色だけが変わります。

### EditorStyles が抱えている実体を塗る

**これが最後の一片でした。** `EditorStyles` はスキンから引いたスタイルを `s_CachedStyles` に
一度だけ抱え込み、以後は作り直しません。こちらの差し替えより前に作られていると、
`EditorStyles.inspectorTitlebar` などは**塗る前の実体を指したまま**になり、画面には
Unity 既定の灰色が出続けます。

気付けたのは原色モードのおかげです。Inspector に中身を出した状態で撮ったところ、
component の見出しだけが原色にならず灰色のまま残りました。`IN Title` は見出しの規則に
当たるのでマゼンタになるはずで、汎用の規則でもシアンになるはずでした。どちらにも
ならないのは「塗った物とは別の物が描かれている」という意味です。

そこで `s_Current` と `s_CachedStyles` が抱えている GUIStyle を直接たどって塗ります。

**作り直させる方法は採りませんでした。** `s_CachedStyles` を空にして
`EditorGUIUtility.SkinChanged()` を呼べば抱え直させられますし、色も直りました。
ただしレイアウトの途中でスタイルの寸法が変わるため、IMGUI が壊れます。

```
ArgumentException: Getting control 0's position in a group with only 0 controls when doing repaint
```

実際に撮った画面では Hierarchy も Project も Inspector も中身が消えました。
色が合っていても中身が出ないのでは本末転倒なので、作り直させず、抱えている実体を
そのまま塗る形にしています。寸法は動かないのでレイアウトは壊れません。

## 開発用: 実際に描画して確かめる

`.devcontainer/unity/run-capture.sh` で、コンテナの中でエディタを実際に描画させて画面を
PNG に撮れます。batchmode では窓が無く何も描かれないので、Xvfb の仮想ディスプレイに GUI の
Unity を立ち上げ、Unity 自身の `InternalEditorUtility.ReadScreenPixel` で撮らせています
（スクショ用の外部コマンドがコンテナに無いため）。

```
run-capture.sh out.png                 テーマ有効で撮る
run-capture.sh out.png --debug-paint   仕組みごとの原色で撮る
run-capture.sh out.png --trace         何がどこを塗ったかも記録する
```

`--trace` は `PaintTracer` を有効にします。Harmony で `GUIStyle.Draw` と `GUI.DrawTexture` に
入り、大きな矩形を塗った相手の名前・背景テクスチャ・矩形を記録します。`TabWindowBackground` が
中身を覆っていることは、これで見つけました。

```
[TRACE] style 'TabWindowBackground' bg=null rect=0,19 227x319
```

環境変数 `YOZOLAB_TRACE_PAINT=1` のときだけ動くので、利用者の環境では何もしません。

## 注意

- Unity のバージョンによって変数名が変わることがあります。2022.3 で確認しています。
- IMGUI 側は「無彩色の灰色の文字」と「名前に selection / title を含むスタイルの背景」を
  対象にした一次近似です。細部の調整は実際に見ながらになります。
