using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YozoLab.EditorTheme;

namespace YozoLab.Tests
{
    /// <summary>
    /// 置いた単色テクスチャが、そのスタイルの 9 スライス枠を切り出せる大きさかを見張る。
    ///
    /// 1x1 を置いていた頃、border を持つスタイル（dockarea は 6,0,6,4）では何も描かれず、
    /// 「差し替えは全部成功しているのに画面が変わらない」になっていた。
    /// </summary>
    public class SolidTextureSizeTests
    {
        [Test]
        public void SolidsKeepTheStockTextureSize()
        {
            // 素の寸法を控える。
            EditorThemeApplier.SetEnabled(false);
            GUISkin skin = ImguiSkinPatcher.EditorDefaultSkin();
            Assert.That(skin, Is.Not.Null);

            var stock = new System.Collections.Generic.Dictionary<GUIStyle, Vector2Int?>();
            foreach (GUIStyle style in skin.customStyles)
            {
                if (style == null) continue;
                Texture2D t = style.normal.background;
                stock[style] = t ? new Vector2Int(t.width, t.height) : (Vector2Int?)null;
            }

            EditorThemeApplier.SetEnabled(true);

            int kept = 0, invented = 0;
            foreach (System.Collections.Generic.KeyValuePair<GUIStyle, Vector2Int?> pair in stock)
            {
                GUIStyle style = pair.Key;
                Texture2D tex = style.normal.background;
                if (tex == null || !tex.name.StartsWith("YozoLab.EditorTheme.Solid")) continue;

                if (pair.Value.HasValue)
                {
                    // 元があったものは、Unity が用意した寸法をそのまま保つ。
                    // Unity 自身、枠より小さいテクスチャを配っていることがある（AppToolbar は
                    // 枠 6 に対し幅 4）ので、「枠より大きいこと」を条件にしてはいけない。
                    kept++;
                    Assert.That(new Vector2Int(tex.width, tex.height), Is.EqualTo(pair.Value.Value),
                        $"'{style.name}' の差し替えで寸法が変わった");
                }
                else
                {
                    // こちらで寸法を決めたものは、9 スライスの枠を切り出せる大きさにする。
                    invented++;
                    RectOffset b = style.border;
                    Assert.That(tex.width, Is.GreaterThan(b.left + b.right), $"'{style.name}' の幅が横枠に足りない");
                    Assert.That(tex.height, Is.GreaterThan(b.top + b.bottom), $"'{style.name}' の高さが縦枠に足りない");
                }
            }

            Debug.Log($"[SIZE] 寸法を保ったもの {kept} 件 / こちらで決めたもの {invented} 件");
            Assert.That(kept, Is.GreaterThan(0), "差し替えたスタイルが 1 つも無い");
        }

        [Test]
        public void DockAreaKeepsItsOriginalTextureSize()
        {
            EditorThemeApplier.SetEnabled(false);
            GUISkin skin = ImguiSkinPatcher.EditorDefaultSkin();
            GUIStyle dockarea = skin.FindStyle("dockarea");
            Assert.That(dockarea, Is.Not.Null);
            Texture2D stock = dockarea.normal.background;
            Assert.That(stock, Is.Not.Null);
            int w = stock.width, h = stock.height;

            EditorThemeApplier.SetEnabled(true);
            Texture2D ours = dockarea.normal.background;
            Debug.Log($"[SIZE] dockarea stock={w}x{h} ours={ours.width}x{ours.height} border={dockarea.border.left},{dockarea.border.top},{dockarea.border.right},{dockarea.border.bottom}");
            Assert.That(ours.name, Does.StartWith("YozoLab.EditorTheme.Solid"));
            Assert.That(ours.width, Is.EqualTo(w), "dockarea の差し替えで幅が変わった");
            Assert.That(ours.height, Is.EqualTo(h), "dockarea の差し替えで高さが変わった");
        }
    }
}
