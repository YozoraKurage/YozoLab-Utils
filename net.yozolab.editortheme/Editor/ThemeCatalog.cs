// 使えるテーマの一覧。組み込みのテーマと、利用者が置いた JSON を集める。
//
// 利用者のテーマは Assets/YozoLabThemes/*.json に置く。プロジェクトの中なので
// 配布物には入らず、書き換えてもパッケージを汚さない。
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace YozoLab.EditorTheme
{
    internal static class ThemeCatalog
    {
        /// <summary>利用者のテーマを置く場所。</summary>
        public const string UserThemeFolder = "Assets/YozoLabThemes";

        private static List<ThemeDefinition> cache;

        /// <summary>組み込み + 利用者のテーマ。</summary>
        public static IReadOnlyList<ThemeDefinition> All
        {
            get
            {
                if (cache == null) Reload();
                return cache;
            }
        }

        public static void Reload()
        {
            cache = new List<ThemeDefinition> { IcebergDark(), IcebergLight() };
            cache.AddRange(PresetThemes.All());

            try
            {
                if (Directory.Exists(UserThemeFolder))
                {
                    foreach (string path in Directory.GetFiles(UserThemeFolder, "*.json"))
                    {
                        ThemeDefinition theme = ThemeDefinition.FromJson(File.ReadAllText(path));
                        if (theme == null || string.IsNullOrEmpty(theme.name))
                        {
                            Debug.LogWarning($"[YozoLab Editor Theme] テーマとして読めません: {path}");
                            continue;
                        }
                        cache.RemoveAll(t => t.name == theme.name);
                        cache.Add(theme);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[YozoLab Editor Theme] テーマの読み込みに失敗: {e.Message}");
            }
        }

        /// <summary>名前で引く。無ければ null。</summary>
        public static ThemeDefinition Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (ThemeDefinition t in All)
            {
                if (t.name == name) return t;
            }
            return null;
        }

        /// <summary>
        /// 明暗に応じて使うテーマを決める。選ばれているテーマが要求と合わない場合は
        /// 同じ明暗の組み込みテーマへ落とす（Unity の Editor Theme 切り替えに追随するため）。
        /// </summary>
        public static ThemeDefinition Resolve(bool dark)
        {
            // 明示的に選ばれていれば、明暗が合わなくてもそれを使う。
            // 以前は「明暗が合わない」として差し戻していたが、暗いエディタで明色テーマを
            // 選んでも黙って無視される作りになっていた（選択欄は選ばれたままなので気付けない）。
            ThemeDefinition chosen = Find(EditorThemeApplier.ThemeName);
            if (chosen != null) return chosen;

            foreach (ThemeDefinition t in All)
            {
                if (t.isDark == dark) return t;
            }
            return chosen ?? IcebergDark();
        }

        /// <summary>利用者のテーマとして保存する。</summary>
        public static string Save(ThemeDefinition theme)
        {
            Directory.CreateDirectory(UserThemeFolder);
            string safe = string.Join("_", theme.name.Split(Path.GetInvalidFileNameChars()));
            string path = Path.Combine(UserThemeFolder, safe + ".json");
            File.WriteAllText(path, theme.ToJson());
            AssetDatabase.Refresh();
            Reload();
            return path;
        }

        private static ThemeDefinition IcebergDark() => new ThemeDefinition
        {
            name = "Iceberg Dark",
            isDark = true,
            accent = "#84a0c6",
            accentBright = "#91acd1",
            control = "#3d425b",
            controlBright = "#444b71",
            controlBrightest = "#5b6389",
            error = "#e27878",
            errorBright = "#e98989",
            info = "#89b8c2",
            infoBright = "#95c4ce",
            purple = "#a093c7",
            selection = "#2a3158",
            success = "#b4be82",
            surface = "#161821",
            surfaceDeepest = "#0f1117",
            surfaceHover = "#272c42",
            surfaceRaised = "#1e2132",
            text = "#c6c8d1",
            textBright = "#d2d4de",
            textBrightest = "#eff0f4",
            textDim = "#818596",
            textMuted = "#9a9ca5",
            warning = "#e2a478",
            warningBright = "#e9b189",
            controlSurface = "#3d425b",
            controlSurfaceSelected = "#5b6389",
            textFaint = "#6b7089",
        };

        private static ThemeDefinition IcebergLight() => new ThemeDefinition
        {
            name = "Iceberg Light",
            isDark = false,
            accent = "#2d539e",
            accentBright = "#4f6ca8",
            control = "#757ca3",
            controlBright = "#9fa7bd",
            controlBrightest = "#8b98b6",
            error = "#cc517a",
            errorBright = "#d96f92",
            info = "#3f83a6",
            infoBright = "#5f9ab7",
            purple = "#7759b4",
            selection = "#a7b2cd",
            success = "#668e3d",
            surface = "#e8e9ec",
            surfaceDeepest = "#cad0de",
            surfaceHover = "#c9cdd7",
            surfaceRaised = "#dcdfe7",
            text = "#33374c",
            textBright = "#33374c",
            textBrightest = "#e8e9ec",
            textDim = "#8389a3",
            textMuted = "#6b7089",
            warning = "#c57339",
            warningBright = "#d18a55",
            controlSurface = "#cad0de",
            controlSurfaceSelected = "#a7b2cd",
            textFaint = "#8389a3",
        };
    }
}
