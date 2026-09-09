つめあわせ

## 導入

VPM パッケージ `net.yozolab.yozolab-utils` として、まとめて入れる形が正規の導入方法です。
同梱アドオンのコンパイル可否は `YozoLab/Utils Settings` で切り替えます。導入直後は
全アドオンが無効になっているので、要るものを選んでください。

## アドオン単体を UPM で指す（開発・検証用）

各アドオンのフォルダはそれ自身が UPM パッケージの形をしています。Package Manager の
「Add package from git URL」でサブフォルダを直接指せます。

```
https://github.com/YozoraKurage/YozoLab-Utils.git?path=/net.yozolab.particletools#0.6.0
```

`net.yozolab.util-settings` が無い状態でも各アドオンは単体で動きます。asmdef の
defineConstraints が `!YOZOLAB_DISABLE_*` という否定形で、誰も切っていなければ
コンパイルされるためです。設定ウィンドウは「切る」側にだけ関わります。

これは検証や開発のための入口で、配布経路としては想定していません。**バンドルと
個別を同じプロジェクトへ同時に入れないでください。** 同じアセンブリと同じ GUID が
二重になって壊れます。
