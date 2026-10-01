# YozoLab SPS（VRCFury SPS port）

[VRCFury](https://github.com/VRCFury/VRCFury) の SPS（Super Plug Shader）を、NDMF と Modular Avatar の
ビルドの中で動くように移植したもの。VRCFury が入っていなくても動く。

- 移植元: VRCFury `2863a3fea5f2a4f36808a4b7dc8f7e8f106dbc0d`（2026-09-07）
- 必要なもの: VRChat SDK（Avatars）、NDMF 1.14 以上、Modular Avatar 1.18 以上
  （揃っていないプロジェクトでは、このパッケージのエディタ部分はコンパイルされない）

## ライセンス

**このフォルダの中身は MIT ではなく、VRCFury のライセンスに従う。** 全文は
[LICENSE.md](LICENSE.md) にある。

```
VRCFury (c) 2022 Senky
```

VRCFury の Personal License に基づく派生物として、個人目的（非商用）でのみ配布・利用できる。
VRCFury のライセンスでは、寄付を受けることも「商用目的」に含まれる。
このフォルダを再配布するときは、著作権表示とライセンス全文を必ず含めること。

アップロードされたアバターに SPS のシェーダーが含まれることについては、VRCFury ライセンスの
VRCA / VRCW License の条項がそのまま適用される。

## 使い方

| コンポーネント | 用途 |
| --- | --- |
| `YozoLab/SPS/SPS Plug` | 差し込む側 |
| `YozoLab/SPS/SPS Socket` | 受ける側 |
| `YozoLab/SPS/SPS Touch Zone` | 触れられて反応する範囲 |
| `YozoLab/SPS/Touch Sender` | 触れる側の当たり判定を足す |
| `YozoLab/SPS/SPS Options` | SPS のメニューの置き場所・アイコンなど（アバターに一つ） |

作成メニューは `Tools/YozoLab SPS/` と、階層ウィンドウの右クリック `YozoLab SPS/` にある。

### エディタで変形を確かめる（NDMF プレビュー）

NDMF のプレビューが有効なら、シーンビューで Plug が近くの Socket の方へ曲がる様子がそのまま見える
（プレイモードに入らなくてよい）。Plug や Socket を動かせば追従する。
NDMF のプレビューメニューの「SPS Deformation」で切り替えられる。

- アバターには一切手を加えない。プレビューのための Resolver・Socket の目印は、シーンに保存されない
  隠しオブジェクトとして作り、NDMF が作るプレビュー用の複製のマテリアルだけを SPS 加工済みのものに替える。
- SPS は画面を介してデータをやり取りするため、シーンビューのカメラが HDR で描いている必要がある
  （VRChat 向けのプロジェクトの既定の設定なら HDR になっている）。
- 他の NDMF プレビュー（Modular Avatar のメッシュ削除など）でメッシュの頂点数が変わった Plug は、
  変形を見せない。
- 深度アクション・触覚の Contact・OSC など、Animator や Contact が動いて初めて起きることはプレビューされない。
  それらはプレイモードで確かめる。

### VRCFury の SPS から移し替える

`Tools/YozoLab SPS/Migrate from VRCFury SPS` で、選んだアバターの中の VRCFury の SPS コンポーネント
（Plug / Socket / Touch Zone / Touch Sender / SPS Options）を、このパッケージのものに移し替える。
設定（深度アクションなども含む）はそのまま写り、元のコンポーネントは削除される（Undo で戻せる）。
VRCFury がプロジェクトに入っている間しか使えないので、移し替えるときだけ VRCFury を入れて実行する。
他のアバターの SPS / VRCFury SPS / TPS / DPS との相互運用のための取り決め（接触判定のタグ、ライト、
OSC のパラメータ名など）は変えていない。

## VRCFury からの主な変更点

VRCFury Personal License の条件として、改変したことをここに記録する。

### ビルドの仕組み

- VRCSDK のビルドフックではなく、NDMF のプラグインとして動く（Generating フェーズ、
  Modular Avatar より前、NDMF のアニメーター管理を有効にした状態）。
- SPS の生成物は新しいコントローラーに作り、アバターの FX などの**末尾に足すだけ**にした。
  VRCFury はアバターのコントローラーを丸ごと読み込んで書き戻しており、その過程で遷移・レイヤー・
  パラメータを「修正」していたが、それはしない。
- Write Defaults は Modular Avatar の Merge Animator と同じ規則でアバターに合わせる。アバターが
  WD OFF のときは、SPS が動かす値の初期値を書く「SPS Defaults」レイヤーを基底レイヤーの下に置く。
- パラメータは Modular Avatar Parameters、メニューは Modular Avatar Menu Installer として出力し、
  Expression Parameters への追加とメニューの設置（8 項目を超えたときのページ送りを含む）は MA に任せる。
- 既存のアニメーションに手を入れるのは、SPS の対象に関わるもの（Plug のレンダラーを作り直したときの
  向け直し、Plug / Socket の有効状態を知るための AAP の追加）だけ。変わったカーブだけを書き戻す。
- 生成物はディスクへ書かず、NDMF の保存に任せる。SPS が加工したシェーダーだけは
  `Assets/YozoLab SPS Generated/` に書き出す（消してかまわない。ビルドのたびに作り直す）。

### 持ち込んでいないもの

SPS と関係なくアバター全体を書き換える、VRCFury 特有の処理は持ち込んでいない。

- Write Defaults の統一、レイヤーの最適化・平坦化、パラメータ圧縮、空レイヤーの削除
- コンストレイントの変換、Quest 非対応マテリアルの削除、オーディオ・ミップマップの修正
- 既定の Additive レイヤーの削除、既存の "Holes" / "Sockets" メニューの移動
- エディタ起動時に Unity / VRCSDK へ当てるパッチ、パッケージの削除、プレハブの上書きの自動復元
  （Harmony は一切使っていない。VRCFury の「Harmony が使えるかの試しのパッチ」も、ドメインリロード中に
  Unity を落とすことがあったので取り除いた）
- VRCFury が自分で作ったオブジェクトを、保存されていなければ次のエディタ更新で破棄する処理
  （SPS の生成物は NDMF に任せており、プレイモードではメモリ上のまま使われるため）
- VRCFury のトグルなど、SPS 以外の機能

### 振る舞いを変えたもの

- スケール変更直後に Contact Receiver を一時停止する処理は、SPS が作った Receiver だけを対象にした
  （VRCFury はアバター上のすべての Receiver を止めていた）。
- 古い DPS / TPS の自動変換（TPS の送信側や古い目印の削除を含む）は、
  `Tools/YozoLab SPS/Settings/Auto-Upgrade DPS with contacts` を ON にしたときだけ行う（既定 OFF）。
  手動の `Tools/YozoLab SPS/Upgrade DPS to SPS` はそのまま使える。
- 衣装などのボーンがどの人型ボーンに付くかの判定は、VRCFury の Armature Link の代わりに
  Modular Avatar の Merge Armature / Bone Proxy を辿る。
- 頭に付いた Plug / Socket を一人称でも見せる処理は、VRCHeadChop に登録するだけにした。
- 深度アクションのうち「まばたきを止める」「口パクを止める」「ジェスチャーを無効化」「World Drop」は、
  VRCFury ではアバター全体の仕組みを書き換えて実現していたため、このポートでは動作しない
  （インスペクタにその旨を表示する）。
- Poiyomi のマテリアルは、SPS が加工する前にロックされる（VRCFury と同じ。Poiyomi 自身も
  アップロード時に同じロックを行う）。
- NDMF のプレビューで、エディタで止まったまま変形を見られるようにした（VRCFury には無い）。
- インスペクターを日本語にし、組み立てを変えた（よく使う設定を上に、タグ・深度アニメーション・触覚・
  古い設定は開け閉めできるまとまりに、注意書きは一番上に）。深度アニメーションは、チェックを外すと
  中身が消える作りだったのを、リストを直接編集する形にした。
- シェーダー `sps_cell_frag.cginc` の画面の行の数え方を、OpenGL / Vulkan でも正しくなるよう
  Direct3D 以外では反転しないようにした（Direct3D での振る舞いは変わらない）。
- 名前空間 `VF` → `YozoLab.SPS`、コンポーネント名（`VRCFuryHapticPlug` → `SpsPlug` など）、
  アセットの GUID、メニューの場所、設定値のキー、隠しシェーダー名を変え、VRCFury と同じプロジェクトに
  入っていても衝突しないようにした。
