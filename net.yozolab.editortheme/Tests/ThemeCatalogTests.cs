using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using YozoLab.EditorTheme;

namespace YozoLab.Tests
{
    /// <summary>
    /// テーマの土台（役割・定義・一覧）の回帰テスト。
    ///
    /// 対応表を「Unity の色 → Iceberg の色」の直書きから「Unity の色 → 役割」に
    /// 置き換えたときの取り決めを固定する。役割の割り当てを書き換えると配色が
    /// 静かに変わるので、代表的な対応を明示的に押さえておく。
    /// </summary>
    public class ThemeCatalogTests
    {
        private static ThemeDefinition Dark => ThemeCatalog.Find("Iceberg Dark");
        private static ThemeDefinition Light => ThemeCatalog.Find("Iceberg Light");

        private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c).ToLowerInvariant();

        private static Color Parse(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            return c;
        }

        [Test]
        public void BuiltInThemesExist()
        {
            Assert.That(Dark, Is.Not.Null, "Iceberg Dark が無い");
            Assert.That(Light, Is.Not.Null, "Iceberg Light が無い");
            Assert.That(Dark.isDark, Is.True);
            Assert.That(Light.isDark, Is.False);
        }

        [Test]
        public void EveryRoleHasAColourInBothBuiltIns()
        {
            foreach (ThemeRole role in Enum.GetValues(typeof(ThemeRole)))
            {
                // 未定義はマゼンタで返る決まりなので、それを検出に使う。
                Assert.That(Dark.Get(role), Is.Not.EqualTo(Color.magenta), $"Iceberg Dark に {role} が無い");
                Assert.That(Light.Get(role), Is.Not.EqualTo(Color.magenta), $"Iceberg Light に {role} が無い");
            }
        }

        /// <summary>
        /// 役割経由でも、以前の直書きの表と同じ結果になること。
        /// 代表的な対応を golden として押さえる。
        /// </summary>
        [TestCase("#383838", "161821")] // 窓の地
        [TestCase("#2A2A2A", "0f1117")] // 最も暗い面
        [TestCase("#3C3C3C", "1e2132")] // 一段明るい面
        [TestCase("#424242", "272c42")] // ホバー
        [TestCase("#2C5D87", "2a3158")] // 選択行
        [TestCase("#585858", "444b71")] // 枠線
        [TestCase("#D2D2D2", "d2d4de")] // 明るい文字
        [TestCase("#FFFFFF", "eff0f4")] // 最も明るい文字
        [TestCase("#D32222", "e27878")] // エラー
        [TestCase("#F4BC02", "e2a478")] // 警告
        public void DarkTranslationMatchesTheOriginalTable(string stock, string expected)
        {
            Color got = ThemeTranslation.Translate(Parse(stock), Dark);
            Assert.That(Hex(got), Is.EqualTo(expected), $"{stock} の行き先が変わった");
        }

        [TestCase("#FFFFFF", "e8e9ec")] // 白は面ではなく文字の役割
        [TestCase("#000000", "33374c")] // 本文
        [TestCase("#EDEDED", "e8e9ec")] // 窓の地
        [TestCase("#3A72B0", "a7b2cd")] // 選択行
        public void LightTranslationMatchesTheOriginalTable(string stock, string expected)
        {
            Color got = ThemeTranslation.Translate(Parse(stock), Light);
            Assert.That(Hex(got), Is.EqualTo(expected), $"{stock} の行き先が変わった");
        }

        [Test]
        public void UnknownChromaticColoursArePassedThrough()
        {
            // 意味を持っている可能性があるので触らない。
            Color odd = new Color(0.13f, 0.77f, 0.31f);
            Assert.That(ThemeTranslation.Translate(odd, Dark), Is.EqualTo(odd));
        }

        [Test]
        public void UnknownGreysSnapToTheNearestRole()
        {
            // 表に無い灰色。明るさの近い項目の役割へ寄る。
            Color grey = new Color(0.226f, 0.226f, 0.226f); // #3A3A3A 相当
            Color got = ThemeTranslation.Translate(grey, Dark);
            Assert.That(got, Is.Not.EqualTo(grey), "表に無い灰色が素通りしている");
            Assert.That(got, Is.Not.EqualTo(Color.magenta), "役割が引けていない");
        }

        [Test]
        public void JsonRoundTripKeepsEveryRole()
        {
            ThemeDefinition clone = ThemeDefinition.FromJson(Dark.ToJson());
            Assert.That(clone, Is.Not.Null, "JSON から戻せない");
            Assert.That(clone.name, Is.EqualTo(Dark.name));
            Assert.That(clone.isDark, Is.EqualTo(Dark.isDark));
            foreach (ThemeRole role in Enum.GetValues(typeof(ThemeRole)))
                Assert.That(clone.Get(role), Is.EqualTo(Dark.Get(role)), $"{role} が往復で変わった");
        }

        [Test]
        public void PaletteIsDerivedFromTheTheme()
        {
            IcebergPalette.Palette p = Dark.ToPalette();
            Assert.That(Hex(p.Background), Is.EqualTo("161821"));
            Assert.That(Hex(p.BackgroundDark), Is.EqualTo("0f1117"));
            Assert.That(Hex(p.Menu), Is.EqualTo("3d425b"));
            Assert.That(Hex(p.ForegroundDim), Is.EqualTo("6b7089"));
            Assert.That(p.IsDark, Is.True);

            IcebergPalette.Palette l = Light.ToPalette();
            Assert.That(Hex(l.Menu), Is.EqualTo("cad0de"), "明色の Menu は面の色");
            Assert.That(Hex(l.ForegroundDim), Is.EqualTo("8389a3"));
        }

        /// <summary>
        /// 組み込みのテーマが全部そろっていて、読める配色になっていること。
        /// 役割の埋め忘れや、面と文字が同化した配色を弾く。
        /// </summary>
        [Test]
        public void EveryBuiltInThemeIsCompleteAndLegible()
        {
            float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

            var problems = new System.Collections.Generic.List<string>();
            foreach (ThemeDefinition theme in ThemeCatalog.All)
            {
                foreach (ThemeRole role in Enum.GetValues(typeof(ThemeRole)))
                {
                    if (theme.Get(role) == Color.magenta)
                        problems.Add($"{theme.name}: {role} が未定義");
                }

                // 本文と地の明度差。これが無いと文字が読めない。
                float gap = Mathf.Abs(Luminance(theme.Get(ThemeRole.Text)) - Luminance(theme.Get(ThemeRole.Surface)));
                if (gap < 0.25f) problems.Add($"{theme.name}: 本文と地の明度差が小さい ({gap:F2})");

                // へこんだ面は地より暗い。これは明暗どちらのテーマでも同じで、
                // 明色テーマでも surfaceDeepest（入力欄の凹みなど）は地より暗くなる。
                float deepest = Luminance(theme.Get(ThemeRole.SurfaceDeepest));
                float surface = Luminance(theme.Get(ThemeRole.Surface));
                if (deepest > surface + 0.01f)
                    problems.Add($"{theme.name}: surfaceDeepest が surface より明るい");
            }

            foreach (string p in problems.Take(10)) Debug.Log($"[THEME] {p}");
            Assert.That(problems, Is.Empty, $"{problems.Count} 件の問題");
        }

        [Test]
        public void PresetThemesAreListed()
        {
            string[] names = ThemeCatalog.All.Select(t => t.name).ToArray();
            foreach (string expected in new[] { "Iceberg Dark", "Dracula", "Nord", "Gruvbox Dark",
                                                "Solarized Light", "Tokyo Night", "Monokai" })
                Assert.That(names, Contains.Item(expected), $"{expected} が一覧に無い");

            Assert.That(names.Distinct().Count(), Is.EqualTo(names.Length), "名前が重複している");
            Assert.That(names.Count(n => ThemeCatalog.Find(n).isDark == false),
                Is.GreaterThanOrEqualTo(3), "明色テーマが少なすぎる");
        }

        /// <summary>
        /// 明示的に選んだテーマは、明暗が合わなくても使われること。
        /// 暗いエディタで明色テーマを選んでも黙って無視されていた不具合の回帰テスト。
        /// </summary>
        [Test]
        public void ExplicitChoiceWinsOverDarkness()
        {
            string saved = EditorThemeApplier.ThemeName;
            try
            {
                EditorThemeApplier.ThemeName = "Solarized Light";
                ThemeDefinition got = ThemeCatalog.Resolve(true); // 素は暗色
                Assert.That(got.name, Is.EqualTo("Solarized Light"), "選んだ明色テーマが差し戻された");
                Assert.That(got.isDark, Is.False);
            }
            finally { EditorThemeApplier.ThemeName = saved; }
        }

        [Test]
        public void StockTableIsChosenByStockDarknessNotTheme()
        {
            // 素が暗色なら、明色テーマでも暗色側の表を引く。
            // #383838 は暗色の表にしかない。明色テーマの面の色へ写るのが正しい。
            Color got = ThemeTranslation.Translate(Parse("#383838"), Light, stockDark: true);
            Assert.That(Hex(got), Is.EqualTo(Hex(Light.Get(ThemeRole.Surface))),
                "素の表がテーマの明暗で選ばれてしまっている");
        }

        [Test]
        public void ResolveFallsBackToMatchingDarkness()
        {
            // 存在しない名前を選んでも、明暗の合う組み込みへ落ちる。
            string saved = EditorThemeApplier.ThemeName;
            try
            {
                EditorThemeApplier.ThemeName = "存在しないテーマ";  // 見つからないときだけ明暗で選ぶ
                Assert.That(ThemeCatalog.Resolve(true).isDark, Is.True);
                Assert.That(ThemeCatalog.Resolve(false).isDark, Is.False);
            }
            finally { EditorThemeApplier.ThemeName = saved; }
        }
    }
}
