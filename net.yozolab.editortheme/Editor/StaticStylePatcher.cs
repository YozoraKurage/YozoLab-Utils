using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace YozoLab.EditorTheme
{
    /// <summary>
    /// Unity のエディタクラスが private static に抱えている GUIStyle のコピーを塗り替える。
    ///
    /// GUISkin を書き換えても画面が変わらなかった理由がこれ。HostView は
    /// <c>Styles.background = new GUIStyle("hostview")</c> のように**スキンからのコピー**を
    /// static に持ち、それで窓全体を塗っている。DockArea のタブ（"dragtab"）や
    /// 見出し（"dockHeader"）も同じ。コピーは型の初期化時に作られ、以後スキンとは無関係。
    /// スキンの原本を書き換えても、既に作られたコピーには何も起きない。
    ///
    /// そこで UnityEditor 系アセンブリの全型から、GUIStyle 型の static フィールドを
    /// 列挙し、スキンと同じ規則（名前で窓枠を判定、灰色の文字色を写す）を当てる。
    /// コピーは元の名前（"hostview" など）を保っているので、名前での判定がそのまま効く。
    ///
    /// static フィールドを読むと型初期化子が走る。Styles 系の入れ子型はスタイルを作る
    /// だけなので害は無いが、念のため入れ子型に限り、初期化に失敗した型は飛ばす。
    /// </summary>
    internal static class StaticStylePatcher
    {
        private const BindingFlags StaticAny = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static readonly List<Type> Candidates = new List<Type>();
        private static bool candidatesCollected;

        /// <summary>塗り替えた static GUIStyle の数。診断用。</summary>
        public static int PatchedCount { get; private set; }

        /// <summary>塗り替えた static Color の数。診断用。</summary>
        public static int PatchedColorCount { get; private set; }

        private static readonly List<KeyValuePair<FieldInfo, Color>> ColorBackups =
            new List<KeyValuePair<FieldInfo, Color>>();

        /// <summary>
        /// エディタクラスが static に抱えている Color を塗り替える。
        ///
        /// スタイルと同じ問題がここにもある。たとえば Hierarchy の可視性列は
        ///
        ///     public static readonly Color backgroundColor =
        ///         EditorResources.GetStyle("game-object-tree-view-scene-visibility")...
        ///     using (new GUI.BackgroundColorScope(Styles.backgroundColor))
        ///
        /// のように、型初期化のときカタログから読んだ色を static に抱え込む。こちらが
        /// カタログを塗り替えるのはそれより後なので、抱え込まれた値は素のまま残る。
        /// 個別に狙い撃ちせず、抱え込まれた色をまとめて翻訳表に通す。
        ///
        /// 色付きの値（警告の黄、エラーの赤など）は翻訳表が素通しするので触らない。
        /// </summary>
        internal static void PatchStaticColors(bool dark)
        {
            RestoreStaticColors();
            CollectCandidates();

            using (new SkinScope())
            {
                foreach (Type type in Candidates)
                {
                    FieldInfo[] fields;
                    try { fields = type.GetFields(StaticAny); }
                    catch (Exception) { continue; }

                    foreach (FieldInfo field in fields)
                    {
                        if (field.FieldType != typeof(Color)) continue;

                        Color value;
                        try { value = (Color)field.GetValue(null); }
                        catch (Exception) { continue; }

                        Color mapped = IcebergTranslation.Translate(value, dark);
                        if (mapped == value) continue;

                        try
                        {
                            field.SetValue(null, mapped);
                            ColorBackups.Add(new KeyValuePair<FieldInfo, Color>(field, value));
                        }
                        catch (Exception) { /* readonly を書けない実行環境なら諦める */ }
                    }
                }
            }

            PatchedColorCount = ColorBackups.Count;
        }

        internal static void RestoreStaticColors()
        {
            for (int i = ColorBackups.Count - 1; i >= 0; i--)
            {
                try { ColorBackups[i].Key.SetValue(null, ColorBackups[i].Value); }
                catch (Exception) { }
            }
            ColorBackups.Clear();
            PatchedColorCount = 0;
        }

        /// <summary>
        /// 対象の GUIStyle を全部列挙する。1 つのスタイルが複数の static から参照されて
        /// いることがあるので、同一性で重複を除く。
        /// </summary>
        public static IEnumerable<GUIStyle> EnumerateStyles()
        {
            CollectCandidates();

            // static を読むと型初期化子が走り、new GUIStyle("hostview") のような行が
            // GUISkin.current からスタイルを引く。GUI 文脈の外では current が無く、
            // "StyleNotFoundError" の空のコピーが作られてそのまま居座る（batchmode で実際に
            // 起きた）。列挙のあいだだけ current をエディタのスキンにしておく。
            using (new SkinScope())
            {
                var seen = new HashSet<GUIStyle>();
                foreach (GUIStyle style in EnumerateStylesCore(seen)) yield return style;
                PatchedCount = seen.Count;
            }
        }

        private static IEnumerable<GUIStyle> EnumerateStylesCore(HashSet<GUIStyle> seen)
        {
            foreach (Type type in Candidates)
            {
                FieldInfo[] fields;
                try { fields = type.GetFields(StaticAny); }
                catch (Exception) { continue; }

                foreach (FieldInfo field in fields)
                {
                    if (field.FieldType != typeof(GUIStyle)) continue;

                    GUIStyle style;
                    try { style = field.GetValue(null) as GUIStyle; }
                    catch (Exception) { continue; } // 型初期化子が GUI 文脈を要求して失敗する型がある

                    if (style != null && seen.Add(style)) yield return style;
                }
            }
        }

        /// <summary>
        /// UnityEditor 系アセンブリの、名前が Styles / Style / Constants / Content の入れ子型。
        /// Unity のエディタコードがスタイルのコピーを置く場所はほぼこの命名に揃っている。
        /// </summary>
        private static void CollectCandidates()
        {
            if (candidatesCollected) return;
            candidatesCollected = true;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = assembly.GetName().Name;
                if (!name.StartsWith("UnityEditor", StringComparison.Ordinal)) continue;

                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                catch (Exception) { continue; }

                foreach (Type type in types)
                {
                    if (type == null || !type.IsNested) continue;
                    switch (type.Name)
                    {
                        case "Styles":
                        case "Style":
                        case "Constants":
                        case "Content":
                        case "Contents":
                            Candidates.Add(type);
                            break;
                    }
                }
            }
        }

        private sealed class SkinScope : IDisposable
        {
            // GUISkin.current は internal static な **フィールド**（プロパティではない。実測）。
            private static readonly FieldInfo CurrentField = typeof(GUISkin).GetField("current",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            private static readonly MethodInfo MakeCurrent = typeof(GUISkin).GetMethod("MakeCurrent",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            private readonly object previous;
            private readonly bool changed;

            public SkinScope()
            {
                if (CurrentField == null) return;
                try
                {
                    previous = CurrentField.GetValue(null);
                    // GUI の中から呼ばれていて current がエディタのスキンなら触らない。
                    // null のほか、batchmode では GameSkin（"hostview" を持たない）が入っている
                    // ことがあり、そのまま列挙すると型初期化子が StyleNotFoundError のコピーを
                    // 作って居座る（実測）。名前で判定する。
                    if (previous is GUISkin skin && skin.FindStyle("hostview") != null) return;

                    GUISkin editorSkin = ImguiSkinPatcher.EditorDefaultSkin();
                    if (editorSkin == null) return;
                    CurrentField.SetValue(null, editorSkin);
                    MakeCurrent?.Invoke(editorSkin, null);
                    changed = true;
                }
                catch (Exception) { changed = false; }
            }

            public void Dispose()
            {
                if (!changed) return;
                try
                {
                    CurrentField.SetValue(null, previous);
                    if (previous is GUISkin skin) MakeCurrent?.Invoke(skin, null);
                }
                catch (Exception) { }
            }
        }
    }
}
