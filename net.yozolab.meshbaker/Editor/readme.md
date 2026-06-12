# Mesh Baker

指定した複数のRenderer（SkinnedMeshRenderer/MeshRenderer）を、**現在のポーズのまま1つの静的メッシュ**に
非破壊でベイクするツールです。元のレンダラーには一切手を加えず、
成果物（Mesh / アトラステクスチャ / Material）は別アセットとして出力されます。
ベイクに必要な設定は `MeshBakeAssembly` コンポーネントとして
GameObjectにシリアライズされ、シーン/プレハブに保存されます。

## 使い方

1. 空のGameObject（ベイク結果の基準になる位置）に
   `YozoLab > Mesh Bake Assembly` コンポーネントを追加する
2. `Renderer Groups` にグループを作り、ベイクしたいRendererを登録する
   （SkinnedMeshRenderer・MeshRendererのどちらも可）。
   **グループごとに別の静的Mesh/プレハブとして出力**され、
   マテリアルとアトラステクスチャは全グループで共有されます
   （1つの出力にまとめたい場合はグループを1つにする）
3. 必要に応じて「マテリアルを検査」で統合時の問題を事前チェックする
4. 「静的メッシュにベイク」を押す

SkinnedMeshRendererは現在のポーズ・ブレンドシェイプの状態をそのまま焼き込みます
（`BakeMesh`を使用）。MeshRendererはそのTransform（スケール含む）を反映して焼き込みます。

PlayMode Pose Bakerで揺れ物のポーズを焼き込んだ後にベイクすれば、
「揺れ物が落ち着いた状態の置物」を作る、といった使い方もできます。

## 出力

`Output Directory` に以下が出力されます（ベース名は `Output Name`、空ならGameObject名）:

- `<名前>_<グループ名>_mesh.asset` — グループごとの結合済み静的Mesh
- `<名前>_<グループ名>_Baked.prefab` — グループごとのMeshRendererプレハブ
- `<名前><プロパティ名>.png` — アトラステクスチャ（マテリアル統合時・全グループ共有）
- `<名前>_mat.mat` — 統合マテリアル（マテリアル統合時・全グループ共有）

グループ名が空の場合は連番（`Group1`など）になり、
グループが1つだけで無名の場合はサフィックスなしの従来の名前になります。

同名アセットが既にある場合はGUIDを維持したまま中身が差し替えられるため、
再ベイクしても既存の参照は壊れません。プレハブも `LoadPrefabContents` による
中身だけの更新を行うため、プレハブ自体のGUIDと内部コンポーネントのfileIDが
維持され、**プレハブ一式をそのまま配布しても参照整合性が保たれます**。
`Create Scene Object` が有効な場合、コンポーネントの子としてこのプレハブの
インスタンスが配置されます（`Mark Static` でstatic化）。

## マテリアルモード（Autodesk Interactive）

`Material Mode` は、どのテクスチャプロパティをアトラス対象にするかを切り替える
Editor上の抽象化です。

- **Custom**（既定）: `Texture Properties` に手動指定したプロパティを使います。
- **Autodesk Interactive**: Autodesk Interactiveシェーダーのテクスチャプロパティ
  （Albedo `_MainTex` / Metallic `_MetallicGlossMap` / Roughness `_SpecGlossMap` /
  Normal `_BumpMap` / Occlusion `_OcclusionMap` / Emission `_EmissionMap`）を
  **自動で収集し、それぞれをアトラス化**します。`Texture Properties` の手動指定は不要です。
  （Autodesk InteractiveシェーダーはUnity Standardと同じプロパティ名を使います）。
  実際にいずれかのマテリアルが持つマップだけを対象にするため、未使用マップで
  空アトラスが作られることはありません。
  統合マテリアルは**確実にAutodesk Interactiveシェーダー**として出力され
  （ベースがTransparent/Maskedバリアントの場合はそれを維持）、
  割り当てた各マップに対応するシェーダーキーワード
  （`_METALLICGLOSSMAP` / `_SPECGLOSSMAP` / `_NORMALMAP` / `_EMISSION`）を
  自動で有効化します。これによりノーマル・オクルージョン・発光（ライトベイク用）も
  結合後に正しく反映されます。発光マップがある場合は GI 寄与（BakedEmissive）も設定します。

## マテリアル統合とUV最適化

- 全マテリアルのテクスチャを1枚のアトラスに統合し、1マテリアルにまとめます。
  統合マテリアルのベースは `Merge Base Material` で指定でき、
  そのマテリアルの複製にアトラステクスチャを差し込む形で生成されます
  （シェーダーやスカラー系プロパティはベースマテリアルのものが引き継がれます）。
  未指定の場合は最初に見つかったマテリアルがベースになります。
- `Texture Properties` に列挙したプロパティがアトラス化されます（既定: `_MainTex`）。
  `_BumpMap` などを追加すると、同じレイアウトで各プロパティのアトラスが作られます。
  ノーマルマップ（プロパティ名にBump/Normalを含む）は圧縮形式のスウィズルを
  デコードした上でPNG化し、NormalMapとして再インポートされます。
- `Packing Mode`:
  - **UV Islands**（既定）: UVアイランド単位で詰め直します。
    同一UV座標の頂点をUnion-Findで結合してアイランドに分解し、
    縦長アイランドを90度回転した上で高さ降順のシェルフ詰め
    （NFDH + Floor-Ceiling: 各棚の床側は左から、天井側は右から詰める）でパッキング。
    さらに全体を面積比0.5相当から「収まらなくなる直前」まで拡大する探索を行い、
    アトラスの充填率を最大化します。
    このアルゴリズムは [TexTransTool / TexTransCore](https://github.com/ReinaS-64892/TexTransTool)
    （MIT License, Copyright (c) 2023 Reina_Sakiria）の
    `IslandUtility` / `NFDHPlasFC` / `IslandRelocationManager` を参考に実装しています。
  - **Material Rects**: マテリアルごとの矩形単位でPackTexturesに詰めます（単純・安全）。
- `Optimize UV Bounds`: （Material Rectsモード時）実際に使用されているUV範囲だけを
  テクスチャから切り出してアトラスに詰めます。UV Islandsモードではアイランド単位で
  常に同等の最適化が行われます。
- `Bake Texture ST`: マテリアルのTiling/OffsetをUVに焼き込みます。
- [0,1]を超えるUV（タイリング）は、タイリングされた絵ごとアトラスに焼き込まれます
  （範囲が広いほど解像度が低下します。検査で警告されます）。
- `Generate Lightmap UVs`: 結合メッシュにライトマップ用のUV2を生成します。

## 使用テクスチャ / 解像度設定

「テクスチャを検査」ボタンで、ベイク対象が参照しているメインテクスチャが
**左にサムネイル、右に解像度設定と参照マテリアル一覧**という形で表示されます。
**同じテクスチャを複数のマテリアルが参照している場合は1つにまとめて表示**されるため、
重複なく見渡せます。メインテクスチャを持たないマテリアルは末尾に一覧されます。

### テクスチャ解像度の上書き（非破壊）

各テクスチャの「目標解像度」を変更すると、そのテクスチャが
アトラス上で占めるテクセル密度（=実効解像度）を下げられます。
**元のテクスチャアセットには一切手を加えません**。設定は
`MeshBakeAssembly` コンポーネントに（テクスチャ参照と対で）シリアライズされ、
シーン/プレハブに保存されます。テクスチャ単位なので、同じテクスチャを使う
すべてのマテリアルに一度の設定で反映されます。

- 例: 2048pxのテクスチャを 512px に設定すると、アトラス上で
  約1/4の密度（面積では約1/16）になり、空いた領域は他のテクスチャに再配分されます。
- 「原寸（上書きなし）」を選ぶと設定が削除され、デフォルトに戻ります。
- アップスケールはしません（原寸を超える指定は原寸として扱われます）。
- UV Islands / Material Rects のどちらのパッキングモードでも有効です。

## 制限

- マテリアルが`Merge Materials`無効の場合は、マテリアルごとのサブメッシュを持つ
  1メッシュとして出力されます（マテリアルは元のものをそのまま参照）。
- 負のスケールを持つレンダラーは面の向きが反転する場合があります。
- マスク系（Metallicなど）のテクスチャはsRGBとして扱われるため、
  厳密なリニア値が必要な場合は出力PNGのインポート設定を調整してください。
- `MeshBakeAssembly` は通常のMonoBehaviourです。VRChatアバターに
  そのままアップロードしない場合は問題ありませんが、アバター内に置く場合は
  EditorOnlyタグのオブジェクトに付けることを推奨します。

## クレジット

本ツールのUVアイランド分解・アトラスパッキングの内部アルゴリズム
（Union-FindによるUV→アイランド分解、NFDH Plus Floor-Ceilingパッキング、
充填率最適化の拡大探索）は、**TexTransCore** および **TexTransTool** を参照して
実装したものです。

- [TexTransCore](https://github.com/ReinaS-64892/TexTransCore) — `IslandUtility` / `NFDHPlasFC`
- [TexTransTool](https://github.com/ReinaS-64892/TexTransTool) — `IslandRelocationManager`

いずれも MIT License, Copyright (c) 2023 Reina_Sakiria。
