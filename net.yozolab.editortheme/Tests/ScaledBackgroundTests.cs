using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YozoLab.EditorTheme;

namespace YozoLab.Tests
{
    /// <summary>
    /// HiDPI 用の scaledBackgrounds まで塗り替えられているかを見張る。
    ///
    /// 2 倍表示の実機で、1 倍の background だけ差し替えても画面が変わらなかった。
    /// 描画側は画面が 2 倍なら scaledBackgrounds を使うため。ここが素のままだと
    /// 「内部的には当たっているのに見た目が変わらない」が再発する。
    /// </summary>
    public class ScaledBackgroundTests
    {
        private static readonly string[] ChromeNames = { "hostview", "dockarea", "dragtab", "dockHeader" };

        [Test]
        public void ChromeStylesCarryOurTextureAtBothScales()
        {
            EditorThemeApplier.SetEnabled(true);
            GUISkin skin = ImguiSkinPatcher.EditorDefaultSkin();
            Assert.That(skin, Is.Not.Null);

            foreach (string name in ChromeNames)
            {
                GUIStyle style = skin.FindStyle(name);
                if (style == null) continue;

                GUIStyleState state = style.normal;
                Texture2D[] scaled = state.scaledBackgrounds;
                Debug.Log($"[SCALED] {name}: background={(state.background ? state.background.name : "null")} "
                          + $"scaled=[{(scaled == null ? "null" : string.Join(", ", System.Linq.Enumerable.Select(scaled, t => t ? t.name : "null")))}]");

                Assert.That(state.background, Is.Not.Null, $"{name} の背景が空");
                Assert.That(state.background.name, Does.StartWith("YozoLab.EditorTheme.Solid"), $"{name} の 1 倍背景に当たっていない");

                if (scaled == null || scaled.Length == 0) continue;
                foreach (Texture2D t in scaled)
                {
                    Assert.That(t, Is.Not.Null, $"{name} の 2 倍背景が空");
                    Assert.That(t.name, Does.StartWith("YozoLab.EditorTheme.Solid"), $"{name} の 2 倍背景に当たっていない");
                }
            }
        }

        [Test]
        public void DisablingRestoresBothScales()
        {
            EditorThemeApplier.SetEnabled(true);
            GUISkin skin = ImguiSkinPatcher.EditorDefaultSkin();
            GUIStyle style = skin.FindStyle("hostview");
            Assert.That(style, Is.Not.Null);

            EditorThemeApplier.SetEnabled(false);
            try
            {
                GUIStyleState state = style.normal;
                Texture2D[] scaled = state.scaledBackgrounds;
                Debug.Log($"[SCALED] after disable: background={(state.background ? state.background.name : "null")} "
                          + $"scaled=[{(scaled == null ? "null" : string.Join(", ", System.Linq.Enumerable.Select(scaled, t => t ? t.name : "null")))}]");

                Assert.That(state.background?.name, Does.Not.StartWith("YozoLab.EditorTheme.Solid"), "無効化で 1 倍背景が戻っていない");
                if (scaled != null)
                {
                    foreach (Texture2D t in scaled)
                    {
                        Assert.That(t?.name, Does.Not.StartWith("YozoLab.EditorTheme.Solid"), "無効化で 2 倍背景が戻っていない");
                    }
                }
            }
            finally { EditorThemeApplier.SetEnabled(true); }
        }
    }
}
