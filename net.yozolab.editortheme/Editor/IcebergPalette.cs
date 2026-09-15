using UnityEngine;

namespace YozoLab.EditorTheme
{
    /// <summary>
    /// IMGUI 側が使う色。テーマ（ThemeDefinition）の役割から導出する。
    ///
    /// 値そのものはここには持たない。テーマを差し替えれば、この 11 色も一緒に変わる。
    /// </summary>
    internal static class IcebergPalette
    {
        /// <summary>今選ばれている暗色テーマ。</summary>
        public static Palette Dark => ThemeCatalog.Resolve(true).ToPalette();

        /// <summary>今選ばれている明色テーマ。</summary>
        public static Palette Light => ThemeCatalog.Resolve(false).ToPalette();

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
            /// <summary>このテーマが暗色か。</summary>
            public bool IsDark;

            /// <summary>
            /// 今 Unity が使っている素の色が暗色側か。素の色を分類するときに使う。
            /// テーマの明暗とは別物。
            /// </summary>
            public bool StockIsDark;
        }
    }
}
