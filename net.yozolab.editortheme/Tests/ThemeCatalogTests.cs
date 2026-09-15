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

        [Test]
        public void ResolveFallsBackToMatchingDarkness()
        {
            // 存在しない名前を選んでも、明暗の合う組み込みへ落ちる。
            string saved = EditorThemeApplier.ThemeName;
            try
            {
                EditorThemeApplier.ThemeName = "存在しないテーマ";
                Assert.That(ThemeCatalog.Resolve(true).isDark, Is.True);
                Assert.That(ThemeCatalog.Resolve(false).isDark, Is.False);
            }
            finally { EditorThemeApplier.ThemeName = saved; }
        }
    }
}
