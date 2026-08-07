namespace YozoLab.OperationLogger
{
    /// <summary>
    /// ログフォルダに配置する _SCHEMA.md の内容。
    /// ログを読む AI(Claude 等)がスキーマと分析観点を自己完結で把握できるようにする。
    /// OpType / 各コレクタの出力と必ず同期させること。
    /// </summary>
    internal static class SchemaDoc
    {
        public const string Markdown = @"# YozoLab Operation Logger — ログスキーマ & 分析ガイド (schema v1)

このフォルダの `*.jsonl` は Unity Editor 上のユーザー操作記録です(1 行 = 1 イベント)。
目的: AI がこのログを読み、**どの作業で詰まっているか**を分析し、
作業を楽にするエディタ拡張の提案につなげること。

## 共通キー

- `t`  : イベント種別(下表)
- `ts` : 発生時刻 `HH:mm:ss`(コアレス済みイベントは**開始**時刻。日付はヘッダの `date`)
- `n`  : 束ねられた回数(省略時 1)
- `dur`: 所要秒(省略時 ≈0)
- 既定値のキーは省略される(トークン節約)

## イベント種別

| t | 意味 | 主なキー |
|---|---|---|
| `session` | ファイル先頭ヘッダ | `date` `unity` `proj` `scene` `cap`(捕捉能力: menu/ctx/sc) `cont`(リロード/ローテ継続) |
| `prop` | プロパティ変更(コアレス済) | `obj`(階層パス) `comp`(型) `prop` `from` `to` `undo`(操作ラベル) `targets`(多対象一括時) |
| `struct` | 階層構造変更 | `op`: create / destroy / reparent / structure(コンポーネント増減) / reorder / prefab_update、`obj` `from` `to` |
| `sel` | 選択変更(1 秒デバウンス) | `obj`(先頭 5 件) `n`(総数) |
| `undo` | Undo/Redo(連打は 1 行に束ね) | `op`: undo / redo、`label`(初回の操作名) |
| `asset` | アセット操作 | `op`: import / delete / move / save、`paths`(≤10)or `ext`(ヒストグラム)+`note`:bulk |
| `scene` | シーン/プレハブステージ | `op`: open / save / prefab_open / prefab_close、`path` |
| `play` | プレイモード | `op`: enter / exit(exit に `dur` = 滞在秒) |
| `win` | フォーカスウィンドウ変更 | `win` `prev` `prevDur`(前ウィンドウ滞在秒) |
| `tool` | アクティブツール変更 | `tool`(Move/Rotate/Scale/…) |
| `compile` | コンパイル/ドメインリロード | `op`: end(コンパイル秒)/ reload(リロード秒) |
| `err` | Error/Exception(同一メッセージは束ね) | `kind`: error / exception / note、`msg`(160 字切詰) |
| `cmd` | メニュー/ショートカット実行 | `cmd` `via`: menu / context / shortcut |
| `end` | セッション終端 | `dur`(セッション秒) `counts`(種別ごとの件数) |

## 値の表記

- オブジェクトは階層パス(`Avatar/Armature/Hips`)。プレハブ編集モード中は `Prefab:` プレフィクス
- 数値は 3 桁丸め。`.x/.y/.z/.w` 成分は 1 行に合成され `from`/`to` が配列になる
  (4 要素の場合はクォータニオン生値なので注意)
- SkinnedMeshRenderer のブレンドシェイプは `blendShape.<名前>` に解決済み
- `prop` のパスはそれ以外は Unity のシリアライズ名の生値(`m_LocalPosition` など)

## 捕捉の限界(誤読しないこと)

- **メインメニューバーのマウスクリックは捕捉できない**(Unity 2022.3 の構造上の制約)。
  ヘッダ `cap` が true でも取れるのは API/ショートカット経由(`menu`)、
  コンテキストメニュー(`ctx`)、ショートカット(`sc`)のみ。
  メニュー操作の「効果」は `prop`/`struct`/`asset` と `undo` の `label` に現れる
- プレイモード中の編集系イベントは既定で記録されない(`play`/`err` は記録される)
- SceneView 内のマウス移動・カメラ操作は記録されない

## 分析ガイド — フリクション(詰まり)の見つけ方

1. **試行錯誤ループ**: `undo` の `n` が大きい/頻発、同じ `prop` の `from`→`to` が
   往復している(値の振動)→ プレビュー不足・比較手段の不在を疑う
2. **反復手作業(自動化候補)**: 同じ `comp`+`prop` の `prop` 行が別オブジェクトに
   連続する、`targets` 付き行が繰り返される、同じ `cmd` の連打
   → 一括処理ツールの提案チャンス
3. **イテレーションコスト**: `play` enter/exit の周期が短く回数が多い
   (テストループ)+ 各回の `compile`/`reload` の `dur` 合計 → 待ち時間の総量を算出
4. **エラーで停滞**: `err` の直後に操作が止まる/同じ `err` が `n` 回続く
   → 原因調査に詰まっている可能性
5. **探し物**: `win` の切替が高頻度、`sel` が短時間に多数 → 目的のオブジェクト/
   設定場所を探している。検索・ピン留め系ツールの提案チャンス
6. **長い prop の dur + 大きい n**: スライダーを長時間ドラッグ=目視微調整。
   数値入力補助やプリセット化の提案チャンス

提案を出すときは、根拠となるログ行(ts と内容)を引用すること。
";
    }
}
