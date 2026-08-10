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
| Save Baked .anim | ベイク済み Transform クリップを .anim としても保存する |
| Export ASCII | バイナリではなく ASCII FBX で書き出す |
| Frame Rate | サンプリングのフレームレート。0 で元クリップのフレームレートを使用 |
| Bake Root Motion | ルートモーションをルート Transform に焼き込む |
| Bake Scale | スケールカーブもベイクする（既定 OFF） |
| Bake BlendShapes | クリップが動かすブレンドシェイプもベイクする |
| Remove Constant Curves | 値が変化しないカーブを省いて FBX を軽くする。1F ポーズのように全カーブが定数の場合は自動的に無効化される |
| Keyframe Reduction | 直線上に乗るキーを間引いて FBX を軽くする（既定 ON） |
| Reduction Tolerance | 間引きの許容誤差。大きいほど軽く、精度は落ちる |
| Use Other Avatar Definition | サンプリング時に FBX 以外の Avatar を使う |

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

FBX の容量はほとんどがメッシュとブレンドシェイプです。アニメーションだけが欲しい場合は
`Export Content` を `Skeleton Only` にしてください（ボーン階層とカーブだけになります）。
`Bake BlendShapes` が ON のときは参照先が必要なため SkinnedMeshRenderer は残ります。

カーブ側は `Keyframe Reduction` で間引かれます。さらに減らしたいときは
`Reduction Tolerance` を大きく（例: 0.001）、または `Frame Rate` を下げてください。
ベイク結果のカーブ数・キー数・ファイルサイズは実行時に Console へ出力されます。

なお `Export ASCII` を OFF にしてもバイナリにならない場合、その FBX Exporter の
バージョンではエクスポートオプションのメンバ名が異なる可能性があります。
その場合は書き出し後に Console へ警告が出ます。
