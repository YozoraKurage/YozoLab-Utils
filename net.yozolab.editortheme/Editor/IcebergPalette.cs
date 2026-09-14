using UnityEngine;

namespace YozoLab.EditorTheme
{
    /// <summary>
    /// Iceberg (cocopon/iceberg.vim) の配色のうち、C# 側で要る分。
    ///
    /// UI Toolkit 側の色は Themes/*.uss が変数ごと持っている。ここにあるのは
    /// IMGUI の GUISkin を塗り替えるときに使う数色だけで、USS と食い違わないよう
    /// 同じ値を書いている。
    /// </summary>
    internal static class IcebergPalette
    {
        public static readonly Palette Dark = new Palette
        {
            Background = Hex("#161821"),
            BackgroundDark = Hex("#0f1117"),
            Line = Hex("#1e2132"),
            Visual = Hex("#272c42"),
            Selection = Hex("#2a3158"),
            Menu = Hex("#3d425b"),
            MenuSelected = Hex("#5b6389"),
            Foreground = Hex("#c6c8d1"),
            ForegroundBright = Hex("#d2d4de"),
            ForegroundDim = Hex("#6b7089"),
            Blue = Hex("#84a0c6"),
            IsDark = true,
        };

        public static readonly Palette Light = new Palette
        {
            Background = Hex("#e8e9ec"),
            BackgroundDark = Hex("#cad0de"),
            Line = Hex("#dcdfe7"),
            Visual = Hex("#c9cdd7"),
            Selection = Hex("#a7b2cd"),
            Menu = Hex("#cad0de"),
            MenuSelected = Hex("#a7b2cd"),
            Foreground = Hex("#33374c"),
            ForegroundBright = Hex("#33374c"),
            ForegroundDim = Hex("#8389a3"),
            Blue = Hex("#2d539e"),
            IsDark = false,
        };

        /// <summary>
        /// 診断用の原色。テーマではない。
        ///
        /// どの仕組みが画面のどこを塗っているのかを目で確かめるためのもの。実機で
        /// 「差し替えは全部成功しているのに画面が変わらない」が続いたので、推測をやめて
        /// 塗り分けて見るために用意した。仕組みごとに全く違う色を当てるので、
        /// スクリーンショットを 1 枚撮れば持ち主が分かる。
        ///
        ///   赤     hostview / TabWindowBackground / OL box（Surface.Window）
        ///   緑     dockarea / dragtab / dockHeader（Surface.Chrome）
        ///   マゼンタ Toolbar / HelpBox / IN BigTitle（Surface.Toolbar）
        ///   橙     ツールバーの選択状態
        ///   黄     選択行
        ///   シアン 上記以外の GUISkin のスタイル全部（Visual）
        ///   青     UI Toolkit パネルのクリア色（EditorThemeApplier が別途渡す）
        ///   白     OS ウィンドウの地色（同上）
        ///   ピンク テーマシート由来（同上）
        ///
        /// 灰色のまま残る場所は、この 9 つのどれでもない何かが塗っている。
        /// </summary>
        public static readonly Palette Debug = new Palette
        {
            Background = Hex("#ff0000"),
            BackgroundDark = Hex("#00ff00"),
            Line = Hex("#ff00ff"),
            Visual = Hex("#00ffff"),
            Selection = Hex("#ffff00"),
            Menu = Hex("#ff8000"),
            MenuSelected = Hex("#808000"),
            Foreground = Hex("#000000"),
            ForegroundBright = Hex("#000000"),
            ForegroundDim = Hex("#000000"),
            Blue = Hex("#0080ff"),
            IsDark = true,
        };

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color color);
            return color;
        }

        internal sealed class Palette
        {
            public Color Background, BackgroundDark, Line, Visual, Selection, Menu, MenuSelected;
            public Color Foreground, ForegroundBright, ForegroundDim, Blue;
            public bool IsDark;
        }
    }
}
