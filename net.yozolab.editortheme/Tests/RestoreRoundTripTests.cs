using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YozoLab.EditorTheme;

namespace YozoLab.Tests
{
    /// <summary>
    /// 「テーマを適用する」を切ったら、本当に元の状態へ戻るかを機械的に確かめる。
    ///
    /// 見た目だけで判断すると、画面に出ない場所の外し漏れ（カタログの色、型が抱えた色、
    /// スキンのテクスチャ、パネルのクリア色など）を見逃す。適用前・適用後・OFF 後の
    /// 状態を文字列に固めて突き合わせる。
    /// </summary>
    public class RestoreRoundTripTests
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic
                                         | BindingFlags.Static | BindingFlags.Instance;

        private static string Tex(Texture2D t) => t ? t.name : "null";

        /// <summary>カタログの色バッファの要約。</summary>
        private static string CatalogDigest()
        {
            try
            {
                Type res = typeof(Editor).Assembly.GetType("UnityEditor.Experimental.EditorResources");
                object catalog = res?.GetProperty("styleCatalog", Any)?.GetValue(null);
                object buffers = catalog?.GetType().GetProperty("buffers", Any)?.GetValue(catalog);
                if (!(buffers?.GetType().GetField("colors", Any)?.GetValue(buffers) is Color[] colors)) return "catalog=?";
                int hash = 17;
                foreach (Color c in colors) hash = hash * 31 + c.GetHashCode();
                return $"catalog[{colors.Length}]={hash}";
            }
            catch (Exception) { return "catalog=err"; }
        }

        /// <summary>ContainerWindow の地色。</summary>
        private static string ContainerDigest()
        {
            try
            {
                Type cw = typeof(Editor).Assembly.GetType("UnityEditor.ContainerWindow");
                object dark = cw?.GetField("darkSkinColor", Any)?.GetValue(null);
                object light = cw?.GetField("lightSkinColor", Any)?.GetValue(null);
                return $"container={dark}/{light}";
            }
            catch (Exception) { return "container=err"; }
        }

        /// <summary>型が static に抱えている代表的な色。</summary>
        private static string StaticColorDigest()
        {
            try
            {
                Type t = typeof(Editor).Assembly.GetType("UnityEditor.SceneVisibilityHierarchyGUI+Styles");
                object c = t?.GetField("backgroundColor", Any)?.GetValue(null);
                return $"sceneVis={c}";
            }
            catch (Exception) { return "sceneVis=err"; }
        }

        private static string Snapshot()
        {
            GUISkin skin = null;
            try { skin = ImguiSkinPatcher.EditorDefaultSkin(); }
            catch (Exception) { }
            string skinPart = "skin=?";
            if (skin != null)
            {
                GUIStyle host = skin.FindStyle("hostview");
                GUIStyle dock = skin.FindStyle("dockarea");
                GUIStyle tab = skin.FindStyle("TabWindowBackground");
                skinPart = $"hostview={Tex(host?.normal?.background)} dockarea={Tex(dock?.normal?.background)} tabwin={Tex(tab?.normal?.background)}";
            }

            string copyPart;
            try
            {
                Type styles = typeof(Editor).Assembly.GetType("UnityEditor.HostView+Styles");
                var copy = styles?.GetField("background", Any)?.GetValue(null) as GUIStyle;
                copyPart = $"hostCopy={Tex(copy?.normal?.background)}";
            }
            catch (Exception) { copyPart = "hostCopy=err"; }

            string text;
            try
            {
                GUIStyle label = EditorStyles.label;
                text = label?.normal == null
                    ? "label=?"
                    : $"label={ColorUtility.ToHtmlStringRGBA(label.normal.textColor)}";
            }
            catch (Exception) { text = "label=err"; }

            return string.Join(" | ", new[] { skinPart, copyPart, text, CatalogDigest(), ContainerDigest(), StaticColorDigest() });
        }

        [Test]
        public void DisablingRestoresEveryMechanism()
        {
            // 素の状態にしてから測る
            EditorThemeApplier.SetEnabled(false);
            ImguiSkinPatcher.RunPendingInGui();
            string before = Snapshot();
            Debug.Log($"[ROUNDTRIP] before  : {before}");

            EditorThemeApplier.SetEnabled(true);
            ImguiSkinPatcher.RunPendingInGui();
            string applied = Snapshot();
            Debug.Log($"[ROUNDTRIP] applied : {applied}");

            EditorThemeApplier.SetEnabled(false);
            ImguiSkinPatcher.RunPendingInGui();
            string after = Snapshot();
            Debug.Log($"[ROUNDTRIP] after   : {after}");

            Assert.That(applied, Is.Not.EqualTo(before), "適用しても状態が変わっていない。テスト自体が無意味になっている");

            if (after != before)
            {
                string[] b = before.Split('|');
                string[] a = after.Split('|');
                for (int i = 0; i < Math.Min(b.Length, a.Length); i++)
                {
                    if (b[i].Trim() != a[i].Trim())
                        Debug.Log($"[ROUNDTRIP] 戻っていない: 素='{b[i].Trim()}' OFF後='{a[i].Trim()}'");
                }
            }
            Assert.That(after, Is.EqualTo(before), "OFF にしても元の状態に戻っていない");
        }

        /// <summary>
        /// before と after を比べるだけでは、最初から汚れていた値を見逃す。
        /// OFF にした状態に Iceberg の色が残っていないかを直接見る。
        /// </summary>
        [Test]
        public void NoIcebergColourSurvivesDisabling()
        {
            EditorThemeApplier.SetEnabled(true);
            ImguiSkinPatcher.RunPendingInGui();
            EditorThemeApplier.SetEnabled(false);
            ImguiSkinPatcher.RunPendingInGui();

            var palette = new (string name, Color value)[]
            {
                ("Background", IcebergPalette.Dark.Background),
                ("BackgroundDark", IcebergPalette.Dark.BackgroundDark),
                ("Line", IcebergPalette.Dark.Line),
                ("Visual", IcebergPalette.Dark.Visual),
                ("Selection", IcebergPalette.Dark.Selection),
                ("Menu", IcebergPalette.Dark.Menu),
            };

            bool Same(Color a, Color b) =>
                Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f;

            var leaks = new System.Collections.Generic.List<string>();

            // 型が抱えている static Color
            foreach (Type type in StaticColorHolders())
            {
                foreach (FieldInfo f in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType != typeof(Color)) continue;
                    Color v;
                    try { v = (Color)f.GetValue(null); } catch (Exception) { continue; }
                    foreach ((string name, Color value) in palette)
                    {
                        if (Same(v, value)) leaks.Add($"{type.FullName}.{f.Name} = {name}");
                    }
                }
            }

            // スキンのテクスチャ
            GUISkin skin = null;
            try { skin = ImguiSkinPatcher.EditorDefaultSkin(); } catch (Exception) { }
            if (skin?.customStyles != null)
            {
                foreach (GUIStyle st in skin.customStyles)
                {
                    Texture2D bg = st?.normal?.background;
                    if (bg != null && bg.name.StartsWith("YozoLab.EditorTheme.Solid"))
                        leaks.Add($"skin style '{st.name}' がこちらのテクスチャのまま");
                }
            }

            foreach (string l in leaks.Take(20)) Debug.Log($"[LEAK] {l}");
            Assert.That(leaks, Is.Empty, $"OFF にしても {leaks.Count} 件が Iceberg のまま残っている");
        }

        private static System.Collections.Generic.IEnumerable<Type> StaticColorHolders()
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.GetName().Name.StartsWith("UnityEditor", StringComparison.Ordinal)) continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                catch (Exception) { continue; }
                foreach (Type t in types)
                {
                    if (t == null || !t.IsNested) continue;
                    switch (t.Name)
                    {
                        case "Styles": case "Style": case "Constants": case "Content": case "Contents":
                            yield return t; break;
                    }
                }
            }
        }
    }
}
