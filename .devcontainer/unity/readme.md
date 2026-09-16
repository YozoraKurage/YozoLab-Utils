# コンテナ内 Unity Test Runner

このリポジトリの DevContainer には Unity Editor 本体が入っており、
コンテナの中だけで EditMode テストを batchmode 実行できる。

ベースイメージは game-ci の `unityci/editor:ubuntu-2022.3.22f1-base-3`。
machine-id が固定されているので、一度有効化した Personal ライセンス (.ulf) は
コンテナを再ビルドしても使い回せる。

## 構成

- テストプロジェクト本体は **ボリューム** の `/home/node/unity-testproject`
  （`$YOZOLAB_UNITY_PROJECT`）に置く。`/workspace` はホストの bind マウントで
  `Library/` の I/O が桁違いに遅いため、リポジトリ側には `Library/` も `Assets/` も作らない。
- このリポジトリはテストプロジェクトから **ローカルパッケージ**
  (`file:/workspace`) として参照され、`testables` に入っている。
- テストコードはリポジトリの `Tests/`（asmdef: `net.yozolab.yozolab-utils.Tests`）。

## 初回にやること

1. コンテナをビルドすると `postCreateCommand` が `setup.sh` を呼ぶ。
   ライセンスが無ければそこで手順が表示される。
2. ライセンスを有効化する:

   ```bash
   export UNITY_EMAIL='you@example.com'
   read -rs UNITY_PASSWORD && export UNITY_PASSWORD
   .devcontainer/unity/activate-license.sh --login
   ```

   2 要素認証を使っていると `--login` は通らない。その場合は
   `activate-license.sh --create-alf` → ブラウザで .ulf を取得 → `--install` の経路。
   詳しくは引数なしで実行すると出る。
3. `.devcontainer/unity/setup.sh` をもう一度実行する（初回インポートが走る）。

シートを 1 つ消費するので、コンテナを捨てる前に
`activate-license.sh --return` で返却すること。

## テストを走らせる

`.devcontainer/unity` は PATH に入っているので、どこからでも呼べる。

```bash
run-tests.sh                                    # 全件
run-tests.sh --filter '*Baker*'                 # NUnit のフィルタ構文
run-tests.sh --category Slow
run-tests.sh --log                              # 失敗時に Unity ログの末尾も出す
```

標準出力にはサマリと失敗内容だけが出る。生ログと結果 XML は
`$YOZOLAB_UNITY_PROJECT/Logs/` に残る。

## パッケージを足す

| 用途 | コマンド |
| --- | --- |
| UPM（Unity レジストリ） | `add-upm.sh com.unity.formats.fbx` |
| VPM（VRChat SDK / NDMF） | `add-vpm.sh nadena.dev.ndmf` |
| 一覧 | `add-upm.sh --list` / `add-vpm.sh --list` |
| 削除 | `--remove <パッケージ名>` |

どちらも追加後にインポートを一度通してから戻る。

このリポジトリで関係するもの:

- **`com.unity.formats.fbx`** — FBX Animation Baker が書き出しに使う。
  リフレクション経由で呼んでいるので未導入でもコンパイルは通るが、
  実際の書き出しを試すなら要る。
- **`nadena.dev.ndmf`** — posebaker の NDMF 連携。未導入だと
  `net.yozolab.posebaker.Editor` が「存在しないアセンブリを参照している」として
  コンパイルされない（他のアセンブリには影響しない）。posebaker を触るなら入れること。

## テストを足す

`Tests/` に `.cs` を置き、`.cs.meta` も一緒に作る（VPM パッケージなので GUID 必須）。
テスト対象のアセンブリは `Tests/net.yozolab.yozolab-utils.Tests.asmdef` の
`references` に追加する。各ツールの asmdef は `autoReferenced: false` なので、
名前で明示的に参照しないと見えない。

## 注意

- ユーザー提供のデータ（FBX など）は `/workspace/temp~/` に置く。チルダ無しの
  `temp/` はローカルパッケージの一部としてインポートされてしまう。
- `setup.sh` は何度実行してもよい。既にある manifest は上書きせず、
  このリポジトリの参照と `testables` だけを保証する（追加したパッケージは消えない）。

## run-capture.sh

エディタを実際に描画させて画面を PNG に撮る。batchmode では窓が無く何も描かれないので、
Xvfb の仮想ディスプレイに GUI の Unity を立ち上げ、Unity 自身の `ReadScreenPixel` で
撮らせている（スクショ用の外部コマンドがこのイメージに無い）。

```
run-capture.sh out.png                 テーマ有効で撮る
run-capture.sh out.png --debug-paint   仕組みごとの原色で撮る
run-capture.sh out.png --no-theme      テーマ無効で撮る
run-capture.sh out.png --trace         何がどこを塗ったかもログに出す
```

撮影スクリプトの実体は `ScreenCapture.cs.txt`。実行時にテストプロジェクトの
`Assets/Editor/YozoLabScreenCapture.cs` へ複製される。環境変数 `YOZOLAB_CAPTURE` が
無ければ何もしないので、通常の `run-tests.sh` には影響しない。

見た目の変更は、画素を数えて確かめられる。Editor Theme の調査ではこれで
「内部的には当たっているのに画面が変わらない」を切り分けた。
