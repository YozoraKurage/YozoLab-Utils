using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YozoLab.EditorTheme
{
    /// <summary>
    /// Unity のテーマシートの色テーブルを、その場で Iceberg に書き換える。
    ///
    /// ── なぜこの方法か ─────────────────────────────────────────────
    /// Unity 2022 のエディタ UI（窓枠・タブ・ツールバー・UI Toolkit 製の各ウィンドウ）は
    /// 共通のテーマシート 1 枚から色を取る。当初は USS 変数（--unity-colors-*）を
    /// 上書きする作りだったが、実機で測ると変数を書き換えても何も変わらなかった。
    /// 配布されているシートは生成済み（_inter）で、規則側の var() 参照が import 時に
    /// 解決され、色がリテラルで焼き込まれていたためだ。変数は定義だけ残っていて、
    /// Unity 自身の規則はもう参照していない。
    ///
    /// そこで規則が実際に読む先、つまりシートの色テーブル（Color[]）そのものを書き換える。
    /// シートは全ウィンドウで共有されているので 1 回で全部に効き、あとから開いた
    /// ウィンドウにも何もしなくても効く。
    ///
    /// ── 既に開いているウィンドウ ────────────────────────────────────
    /// UI Toolkit は「マッチした規則の組が前回と同じなら計算し直さない」ので、テーブルを
    /// 書き換えただけでは既存の要素は古い色のまま残る。シートの contentHash を変え、
    /// 計算済みスタイルのキャッシュを捨て、各ルートの版を上げると、崩れることなく
    /// 次の描画で追随する（3 つとも要る。実機で 1 つずつ外して確かめた）。
    ///
    /// ── 安全性 ─────────────────────────────────────────────────────
    /// 書き換えるのは管理側（C#）の配列だけで、アセットには保存されない。
    /// ドメインリロードで素の値に戻るので、壊したまま残る心配は無い。
    /// 無効化では控えておいた元の値を戻す。
    /// </summary>
    internal static class ThemeSheetRecolorer
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        private static readonly Type UtilityType = typeof(EditorGUIUtility).Assembly.GetType("UnityEditor.UIElements.UIElementsEditorUtility");
        private static readonly FieldInfo ColorsField = typeof(StyleSheet).GetField("colors", Any);
        private static readonly FieldInfo ContentHashField = typeof(StyleSheet).GetField("m_ContentHash", Any);
        private static readonly MethodInfo IncrementVersion = typeof(VisualElement).GetMethod("IncrementVersion", Any);
        private static readonly object StyleSheetChange = ResolveStyleSheetChange();
        private static readonly MethodInfo ClearStyleCache =
            typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.StyleCache")?.GetMethod("ClearStyleCache", Any);
        private static readonly Type GuiViewType = typeof(EditorGUIUtility).Assembly.GetType("UnityEditor.GUIView");
        private static readonly PropertyInfo ViewVisualTree = GuiViewType?.GetProperty("visualTree", Any);

        private sealed class Patched
        {
            public StyleSheet Sheet;
            public Color[] Original;
            public int OriginalHash;
        }

        private static readonly List<Patched> PatchedSheets = new List<Patched>();

        /// <summary>この Unity で仕組みが成立しているか。内部 API が見つからなければ諦める。</summary>
        public static bool IsSupported =>
            UtilityType != null && ColorsField != null && ContentHashField != null
            && IncrementVersion != null && StyleSheetChange != null;

        /// <summary>今書き換えているシートの数。設定ウィンドウの状態表示用。</summary>
        public static int PatchedCount => PatchedSheets.Count;

        /// <summary>診断用。共通シートの名前と instanceID、書き換え中なら先頭の色。</summary>
        public static string DescribeSheets()
        {
            var sb = new System.Text.StringBuilder();
            foreach (bool dark in new[] { true, false })
            {
                StyleSheet sheet = IsSupported ? GetCommonSheet(dark) : null;
                if (sheet == null) { sb.AppendLine($"  common {(dark ? "dark" : "light")} sheet: null"); continue; }
                var colors = ColorsField.GetValue(sheet) as Color[];
                bool patched = PatchedSheets.Exists(p => p.Sheet == sheet);
                sb.AppendLine($"  common {(dark ? "dark" : "light")} sheet: {sheet.name}#{sheet.GetInstanceID()} colors={colors?.Length} "
                              + $"patched={patched} hash={ContentHashField.GetValue(sheet)} colors[0..3]="
                              + (colors == null ? "?" : string.Join(",", colors.Take(4).Select(c => "#" + ColorUtility.ToHtmlStringRGBA(c)))));
            }
            // 読み込まれている全シートのうち、どれを塗れているか。ウィンドウのルートに
            // 付いていないシート（要素の奥に付いているもの）は今の走査から漏れる。
            var all = Resources.FindObjectsOfTypeAll<StyleSheet>();
            var patchedSet = new HashSet<StyleSheet>();
            foreach (Patched p in PatchedSheets) if (p.Sheet != null) patchedSet.Add(p.Sheet);

            int missed = 0;
            var missedNames = new List<string>();
            foreach (StyleSheet sheet in all)
            {
                if (patchedSet.Contains(sheet)) continue;
                var cols = ColorsField.GetValue(sheet) as Color[];
                if (cols == null || cols.Length == 0) continue;
                missed++;
                if (missedNames.Count < 25) missedNames.Add($"{sheet.name}({cols.Length})");
            }
            sb.AppendLine($"  loaded sheets: {all.Length}, with colours but NOT patched: {missed}");
            sb.AppendLine($"  not patched: {string.Join(", ", missedNames)}");

            sb.Append($"  patched sheets ({PatchedSheets.Count}): "
                      + string.Join(", ", PatchedSheets.Select(p => p.Sheet ? p.Sheet.name : "destroyed")));
            return sb.ToString().TrimEnd();
        }

        /// <summary>dark なら Dark テーマのシート、そうでなければ Light のシートを Iceberg にする。</summary>
        private static bool patchedDark;
        private static Color? patchedDebugColor;

        /// <summary>
        /// <paramref name="debugColor"/> を渡すと、翻訳表を使わず全ての色をその色にする。
        /// 原色モード用。シート由来の画素がどこなのかを一目で分かるようにするため。
        /// 透明度はそのまま残す。透明なものまで塗り潰すと画面が読めなくなる。
        /// </summary>
        public static bool Apply(bool dark, Color? debugColor = null)
        {
            if (!IsSupported) return false;

            Restore();
            patchedDark = dark;
            patchedDebugColor = debugColor;

            StyleSheet common = GetCommonSheet(dark);
            if (common == null) return false;
            if (!PatchSheet(common, dark)) return false;

            SweepAttachedSheets();
            Refresh();
            return true;
        }

        /// <summary>
        /// あとから開いたウィンドウが自前のシートを持ち込むので、定期的に拾って塗り替える。
        /// </summary>
        public static void Sweep()
        {
            if (PatchedSheets.Count == 0) return;
            if (SweepAttachedSheets()) Refresh();
        }

        private static bool SweepAttachedSheets()
        {
            bool any = false;
            foreach (StyleSheet sheet in EnumerateAttachedSheets())
            {
                if (PatchSheet(sheet, patchedDark)) any = true;
            }

            // ウィンドウのルートを辿るだけでは足りない。ツールバーやオーバーレイは
            // 自前のシートを要素の奥に付けていて、この走査から漏れる。実測では
            // ToolbarDark_inter.uss だけで 307 色あり、画面上部の帯がまるごと素のままだった。
            // 読み込まれているシートを直接さらう。
            foreach (StyleSheet sheet in Resources.FindObjectsOfTypeAll<StyleSheet>())
            {
                if (IsOppositeVariant(sheet, patchedDark)) continue;
                if (PatchSheet(sheet, patchedDark)) any = true;
            }
            return any;
        }

        /// <summary>
        /// 今と逆の配色向けのシートか。Dark を当てているときに Light 用まで塗ると、
        /// Unity の Editor Theme を切り替えたときに変な色で残る。
        /// </summary>
        private static bool IsOppositeVariant(StyleSheet sheet, bool dark)
        {
            string name = sheet == null ? string.Empty : sheet.name ?? string.Empty;
            return dark
                ? name.IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0
                : name.IndexOf("Dark", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>1 枚を塗り替えて控える。既に控えてあるものは何もしない。</summary>
        private static bool PatchSheet(StyleSheet sheet, bool dark)
        {
            if (sheet == null) return false;
            if (PatchedSheets.Exists(p => p.Sheet == sheet)) return false;

            var colors = ColorsField.GetValue(sheet) as Color[];
            if (colors == null || colors.Length == 0) return false;

            var patched = new Patched
            {
                Sheet = sheet,
                Original = (Color[])colors.Clone(),
                OriginalHash = (int)ContentHashField.GetValue(sheet),
            };

            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = patchedDebugColor.HasValue
                    ? new Color(patchedDebugColor.Value.r, patchedDebugColor.Value.g, patchedDebugColor.Value.b, colors[i].a)
                    : IcebergTranslation.Translate(colors[i], dark);
            }

            PatchedSheets.Add(patched);
            ContentHashField.SetValue(sheet, patched.OriginalHash ^ 0x1CEBE26);
            return true;
        }

        /// <summary>各ウィンドウのルートに直接付いているシート。</summary>
        private static IEnumerable<StyleSheet> EnumerateAttachedSheets()
        {
            foreach (VisualElement root in EnumerateRoots())
            {
                int count;
                try { count = root.styleSheets.count; }
                catch (Exception) { continue; }

                for (int i = 0; i < count; i++)
                {
                    StyleSheet sheet = null;
                    try { sheet = root.styleSheets[i]; }
                    catch (Exception) { }
                    if (sheet != null) yield return sheet;
                }
            }
        }

        public static void Restore()
        {
            if (PatchedSheets.Count == 0) return;

            foreach (Patched patched in PatchedSheets)
            {
                if (patched.Sheet == null) continue;
                var colors = ColorsField.GetValue(patched.Sheet) as Color[];
                if (colors != null) Array.Copy(patched.Original, colors, Math.Min(colors.Length, patched.Original.Length));
                ContentHashField.SetValue(patched.Sheet, patched.OriginalHash);
            }
            PatchedSheets.Clear();
            Refresh();
        }

        /// <summary>既に開いている全ビューに、書き換えた色で計算し直させる。</summary>
        private static void Refresh()
        {
            ClearStyleCache?.Invoke(null, null);

            foreach (VisualElement root in EnumerateRoots())
            {
                try { IncrementVersion.Invoke(root, new[] { StyleSheetChange }); }
                catch (Exception) { /* 破棄途中のビューは飛ばす */ }
            }

            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        /// <summary>
        /// 全 GUIView のルートと、全 EditorWindow のルート。テーマシートが付いているのは
        /// EditorWindow 側のルートなので、そちらも直接版を上げる。
        /// </summary>
        private static IEnumerable<VisualElement> EnumerateRoots()
        {
            if (GuiViewType != null && ViewVisualTree != null)
            {
                foreach (UnityEngine.Object view in Resources.FindObjectsOfTypeAll(GuiViewType))
                {
                    VisualElement root = null;
                    try { root = ViewVisualTree.GetValue(view) as VisualElement; } catch (Exception) { }
                    if (root != null) yield return root;
                }
            }

            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                VisualElement root = null;
                try { root = window.rootVisualElement; } catch (Exception) { }
                if (root != null && root.panel != null) yield return root;
            }
        }

        private static StyleSheet GetCommonSheet(bool dark)
        {
            MethodInfo getter = UtilityType.GetMethod(dark ? "GetCommonDarkStyleSheet" : "GetCommonLightStyleSheet", Any);
            try { return getter?.Invoke(null, null) as StyleSheet; }
            catch (Exception) { return null; }
        }

        private static object ResolveStyleSheetChange()
        {
            Type changeType = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.VersionChangeType");
            if (changeType == null) return null;
            try { return Enum.Parse(changeType, "StyleSheet"); }
            catch (Exception) { return null; }
        }
    }
}
