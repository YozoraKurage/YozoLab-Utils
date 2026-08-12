using NUnit.Framework;
using YozoLab.FBXAnimationBaker;

namespace YozoLab.Tests
{
    /// <summary>
    /// テスト環境が動いていることを確かめるための最小限のテスト。
    /// エディタ拡張本体は UI とアセット I/O が主なので、まずは
    /// 「設定の既定値が意図どおりか」だけを押さえる。
    /// </summary>
    public class FBXAnimationBakerSettingsTests
    {
        [Test]
        public void NewEntry_HasExpectedDefaults()
        {
            var entry = new AnimationBakeEntry();

            Assert.IsTrue(entry.enabled, "新規エントリは既定で有効");
            Assert.IsTrue(entry.saveBakedClipAsset, "ベイク結果の .anim は既定で保存する");
            Assert.IsTrue(entry.fastImport, "Fast Import は既定で ON");
            Assert.IsFalse(entry.exportAscii, "既定はバイナリ FBX（ASCII は巨大になる）");
            Assert.AreEqual(BakedFbxAnimationType.Generic, entry.importAnimationType);
            Assert.AreEqual(BakeExportContent.ModelAndAnimation, entry.exportContent);
        }

        [Test]
        public void NewEntry_CollectionsAreInitialized()
        {
            var entry = new AnimationBakeEntry();

            // null のままだと GUI 側が最初の描画で落ちる。
            Assert.IsNotNull(entry.clips);
            Assert.IsEmpty(entry.clips);
        }

        [Test]
        public void Settings_SingletonIsAvailable()
        {
            var settings = FBXAnimationBakerSettings.instance;

            Assert.IsNotNull(settings);
            Assert.IsNotNull(settings.bakeEntries);
            Assert.IsNotNull(settings.bakeCacheEntries);
        }
    }
}
