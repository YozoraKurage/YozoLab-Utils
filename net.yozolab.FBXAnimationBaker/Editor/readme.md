# FBX Animation Baker

FBX と Humanoid AnimationClip を指定すると、そのクリップを **Transform アニメーションとして
ベイクした状態で同梱した FBX** を書き出すエディタ拡張です。
FBX Animation Extractor（FBX → .anim）と逆向きの、.anim → FBX 方向のツールにあたります。

Humanoid クリップはマッスル空間で記録されているため、そのままでは Unity 外の DCC ツールや
他ゲームエンジンで再生できません。このツールは対象モデルへ実際にクリップをサンプリングし、
各ボーンのローカル TRS（必要ならブレンドシェイプ）を毎フレーム記録して、
Generic な Transform カーブへ変換したうえで FBX に焼き込みます。

## 必要なパッケージ

- **Unity FBX Exporter (`com.unity.formats.fbx`)**
  Package Manager からインストールしてください。未導入の場合はウィンドウ上部に警告が出ます。
  （パッケージ未導入でもコンパイルは通るようリフレクション経由で呼び出しています）

## 使い方

1. `YozoLab > FBX Animation Baker` でウィンドウを開く
2. `Output Directory` に生成 FBX の保存先フォルダを指定する
3. `Add`（または Project で FBX とクリップを選択して `From Selection`）でエントリを作成する
4. エントリの `Source FBX` にモデル、`Humanoid Clips` にベイクしたいクリップを指定する
5. `Execute` を押す

クリップ 1 つにつき FBX を 1 つ出力します。`Output File Name` が空ならクリップ名、
1 エントリに複数クリップがある場合は `<Output File Name>_<クリップ名>.fbx` になります。

## エントリの設定

| 項目 | 説明 |
| --- | --- |
| Source FBX | アニメーションをベイクする対象のモデル（.fbx） |
| Humanoid Clips | ベイクするクリップ。FBX 内蔵クリップも .anim も指定可 |
| Output Override | このエントリだけ別フォルダへ出力する場合に指定 |
| Output File Name | 出力ファイル名（拡張子なし）。空ならクリップ名 |
| Export Content | 生成 FBX に含めるもの。`Skeleton Only` はメッシュ/レンダラーを外し、アニメーションするノード階層だけにする（**FBX が劇的に小さくなる**） |
| Import Animation Type | 生成 FBX を読み込み直すときの Animation Type（既定は Generic） |
| Fast Import | 生成 FBX のインポート時に不要な処理（マテリアル/カメラ/ライト/カーブ再サンプリング等）を省く（既定 ON） |
| Save Baked .anim | ベイク済み Transform クリップを .anim としても保存する |
| Export ASCII | バイナリではなく ASCII FBX で書き出す |
| Rest Pose | アニメーションを評価しないときに FBX が持つ姿勢。`Source FBX Pose` は元の T ポーズ等を保つ／`First Frame` は 0 フレーム目を焼き込む |
| Frame Rate | サンプリングのフレームレート。0 で元クリップのフレームレートを使用 |
| Bake Root Motion | ルートモーションをルート Transform に焼き込む |
| Bake Scale | スケールカーブもベイクする（既定 OFF） |
| Bake BlendShapes | クリップが動かすブレンドシェイプもベイクする |
| Exclude BlendShapes | メッシュからブレンドシェイプデータを取り除いて書き出す。`Bake BlendShapes` が ON のときは無視される |
| Remove Constant Curves | 値が変化しないカーブを省いて FBX を軽くする。1F ポーズのように全カーブが定数の場合は自動的に無効化される |
| Keyframe Reduction | 直線上に乗るキーを間引いて FBX を軽くする（既定 ON） |
| Reduction Tolerance | 間引きの許容誤差。大きいほど軽く、精度は落ちる |
| Use Other Avatar Definition | サンプリング時に FBX 以外の Avatar を使う |

## 基準ポーズ（T ポーズ）の扱い

FBX はノードごとに「静的なローカル変換」を持ち、テイク（アニメーション）のカーブが
再生時にそれを上書きします。つまり FBX には *素のポーズ* と *動き* の 2 つが入っています。
スキニングのバインドポーズはさらに別枠（デフォーマのクラスタ行列）に保存されるため、
ボーンをどのポーズに置いてもバインドポーズは保たれます。

`Rest Pose` はこの「素のポーズ」に何を残すかの選択です。

- **Source FBX Pose**（既定）… 元 FBX の姿勢（多くのモデルでは T ポーズ）をそのまま残し、
  動きは全てカーブ側に持たせます。DCC で開いたときや、生成 FBX を Humanoid として
  再インポートして Avatar を作り直したいときはこちら。
  姿勢を全ノードのカーブが担うため、`Remove Constant Curves` は自動的に無効になります
  （その分カーブ本数は増えますが、`Keyframe Reduction` で静止ノードは 2 キーまで落ちます）。
- **First Frame** … 0 フレーム目の姿勢をノードに焼き込みます。カーブを持たないノードも
  正しい見た目になるので `Remove Constant Curves` と併用でき、ファイルは小さくなります。
  ただし FBX を開いた初期状態は T ポーズではなくアニメーションの先頭姿勢になります。

## 差分スキップ

Source FBX / クリップの依存ハッシュとエントリ設定の署名をキャッシュしており、
どれも変わっていないエントリは `Execute` でスキップされます。
強制的に作り直したいときは `Re-bake All` を押してください。

## 注意

- ベイク中はシーンに一時的にモデルがインスタンス化され、`AnimationMode` でサンプリングされます。
  処理後にインスタンスは破棄されますが、シーンは編集済み扱いになることがあります。
- Humanoid クリップを指定する場合、Source FBX（または Avatar Definition）が
  Humanoid Avatar を持っている必要があります。持っていない場合は警告が出ます。
- 出力される Transform カーブは毎フレームのベイク結果で、補間は線形です。

## FBX が大きいとき

ブレンドシェイプはメッシュ側のデータなので、`Bake BlendShapes` を OFF にしても FBX には入ります。
モデルは含めたいがブレンドシェイプは要らない場合は `Exclude BlendShapes` を ON にしてください
（ブレンドシェイプ抜きのメッシュを一時的に作って差し替えます。元のアセットは変更しません）。

FBX の容量はほとんどがメッシュとブレンドシェイプです。アニメーションだけが欲しい場合は
`Export Content` を `Skeleton Only` にしてください（ボーン階層とカーブだけになります）。
`Bake BlendShapes` が ON のときは参照先が必要なため SkinnedMeshRenderer は残ります。

カーブ側は `Keyframe Reduction` で間引かれます。さらに減らしたいときは
`Reduction Tolerance` を大きく（例: 0.001）、または `Frame Rate` を下げてください。
ベイク結果のカーブ数・キー数・ファイルサイズは実行時に Console へ出力されます。

なお `Export ASCII` を OFF にしてもバイナリにならない場合（Blender は ASCII FBX を読めません）、
その FBX Exporter のバージョンでは形式の指定方法が異なる可能性があります。
書き出し後にファイルヘッダを検査して食い違えば警告を出すので、ウィンドウ右上の
`Exporter Info` を押して Console のログを確認してください。

## インポートが遅いとき

複数本まとめて実行しても、書き出しが全部終わってから 1 度だけインポートされます
（1 本ごとにインポートは走りません）。

インポート設定は書き出し後にまとめて適用し、**実際に設定が変わるものだけ**再インポートします。
そのため新規生成時は 2 回（初回インポート + 設定適用）、
2 回目以降のベイクは .meta に設定が残っているので 1 回だけです。

なお `AssetPostprocessor` を使えば初回インポートで設定を当てられますが、
ポストプロセッサを含むアセンブリが変わるたびに Unity がプロジェクト内の全モデルを
再インポートしてしまうため、この方式は採っていません。

さらに `Fast Import` が ON だと、マテリアル生成・カメラ/ライト・可視性・コンストレイント・
タンジェント計算・カーブの再サンプリング/圧縮を省きます。ベイク済みカーブは
再サンプリングすると精度も落ちるため、基本は ON のままで問題ありません。
