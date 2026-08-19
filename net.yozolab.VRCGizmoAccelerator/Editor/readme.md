# VRC Gizmo Accelerator

PhysBone の SDK ギズモを止め、**選択に関連する PhysBone だけ**を独自の
一括バッチ描画パスで描き直すエディタ拡張です。

導入の入口は YozoLab Utils の設定ウィンドウです。**このパッケージを
コンパイルする設定にした時点で有効になります**（アドオンは既定で全て
コンパイル OFF なので、選んだ人だけが使う形になる）。
`YozoLab > VRC Gizmo Accelerator` のウィンドウで OFF にすれば、
再コンパイル無しでパッチを外して SDK 本来の描画に戻せます。

## 仕組み

### 1. SDK のギズモを Harmony で止める

`VRCPhysBoneEditor.OnDrawGizmos` は [DrawGizmo] で呼ばれる唯一の入口です。
ここに prefix を当てて false を返すと、`InitTransforms`（PhysBone 1 本ごとに
アバター全体を走査する、一番重い前処理）ごと丸ごと飛びます。

`showGizmos` フィールドには触れません。あれはシリアライズされるユーザーの設定で、
インスペクタの Show Gizmos はこれまでどおり効きます。代替パスは
その値を読んで「描くかどうか」を判断するだけです。

### 2. 代替パスで描き直す

SDK は全 PhysBone を毎フレーム描きますが、実際に見たいのは選択まわりだけです。
シーンビューの Repaint ごとに

- 選択階層の配下にある PhysBone 全て（選択 GameObject の真上のものだけ不透明、他は半透明）
- ボーンを直接つまんでいるときは、そのチェーンを持つ上位の PhysBone も（半透明）

だけを組み立て、頂点を 1 つのメッシュに溜めて SetPass 1 回で描きます。
選択が無ければ何も描きません。

表示の可否は SDK のギズモが従う条件をそのまま踏襲します:
シーンビューの Gizmos トグル、Gizmos メニュー内の PhysBone の個別 ON/OFF、
各コンポーネントの Show Gizmos。どれかが OFF ならこちらも描きません。
キャッシュは持ちません。即時描画の**発行回数**（SetPass と GL 呼び出し）が
負荷の正体なので、対象を絞って 1 回で流せば毎回組み立てても十分軽くなります。

形は SDK のギズモに合わせてあり、ボーン線・Collision Radius の
先細りカプセル／球・角度制限（Angle のコーン / Hinge の扇 / Polar の枠）が
同じ位置・同じ大きさ・同じ色で出ます。

「選択していない PhysBone も描く」を入れると SDK 互換の常時表示になります
（その分のコストは掛かります）。

## 他のエディタ拡張から描画に介入する

`IPhysBoneGizmoExtension` を実装して `PhysBoneGizmoPass.Register` すると、
PhysBone ごとの組み立て（Repaint ごと）に割り込めます。

```csharp
class MyExtension : IPhysBoneGizmoExtension
{
    public int Order => 0;

    public void Build(Component physBone, PhysBoneGizmoCanvas canvas)
    {
        // canvas.AddLine / AddWireSphere / AddTaperedCapsule ... で描き足す。
        // canvas.SuppressDefault = true で、この PhysBone の既定形状を消せる。
    }
}
```

PhysBone Radius Gizmo（同リポジトリ）が最初の利用者で、ドラッグ中に
`SuppressDefault` で既定形状を消し、自前のハンドル表示だけを残しています。
