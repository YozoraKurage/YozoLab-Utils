using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YozoLab.UtilSettings
{
    /// <summary>
    /// 各パッケージを「コンパイルするかどうか」の状態を持ち、asmdef へ反映する。
    ///
    /// 仕組み：各パッケージの asmdef には
    ///   "defineConstraints": ["YOZOLAB_ENABLE_XXX"]
    ///   "versionDefines": [{ "name": "Unity", "expression": "", "define": "YOZOLAB_ENABLE_XXX" }]
    /// が入っている。versionDefines の条件 name="Unity" / expression="" は常に真なので、
    /// このエントリが在る限りシンボルが立ち、defineConstraints が満たされてアセンブリが
    /// コンパイルされる。エントリを消すと制約が満たされなくなり、**アセンブリごと
    /// コンパイルされなくなる**。ソースに #if を書く必要がなく、無効化すれば
    /// Harmony への参照も含めて丸ごと消える。
    ///
    /// プロジェクト全体の Scripting Define Symbols は一切汚さない。シンボルはその
    /// asmdef の中だけで有効。
    ///
    /// 設定の正本は asmdef ではなく <c>ProjectSettings/</c> 側に置く。asmdef は
    /// パッケージを入れ直すと出荷時の状態（全て有効）へ戻ってしまうため、
    /// 起動時に正本から復元する。
    /// </summary>
    [InitializeOnLoad]
    internal static class UtilsCompileState
    {
        private const string SettingsFolder = "ProjectSettings/Packages/net.yozolab.yozolab-utils";
        private const string ConfigPath = SettingsFolder + "/enabled-packages.txt";

        private const string AlwaysTrueVersionDefineName = "Unity";

        static UtilsCompileState()
        {
            // アセットデータベースが整うまで待つ。静的コンストラクタの中で
            // AssetDatabase を触ると、初期化の順序によっては失敗する。
            EditorApplication.delayCall += SyncFromConfig;
        }

        // ---------------------------------------------------------------
        // 設定の読み書き
        // ---------------------------------------------------------------

        /// <summary>有効なパッケージ Id の集合を読む。設定が無ければ「全て有効」を書き出して返す。</summary>
        public static HashSet<string> LoadEnabledIds()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var ids = File.ReadAllLines(ConfigPath)
                        .Select(x => x.Trim())
                        .Where(x => x.Length > 0 && !x.StartsWith("#", StringComparison.Ordinal));
                    return new HashSet<string>(ids);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[YozoLab Utils] 設定の読み込みに失敗しました（全て有効として扱います）: {e.Message}");
            }

            var all = new HashSet<string>(UtilsCatalog.Packages.Select(x => x.Id));
            SaveEnabledIds(all);
            return all;
        }

        public static void SaveEnabledIds(HashSet<string> enabled)
        {
            try
            {
                Directory.CreateDirectory(SettingsFolder);

                var lines = new List<string>
                {
                    "# YozoLab Utils: コンパイルするパッケージの一覧。",
                    "# このファイルが正本。asmdef 側はここから復元される。",
                };
                lines.AddRange(UtilsCatalog.Packages.Where(p => enabled.Contains(p.Id)).Select(p => p.Id));

                File.WriteAllLines(ConfigPath, lines);
            }
            catch (Exception e)
            {
                Debug.LogError($"[YozoLab Utils] 設定の保存に失敗しました: {e.Message}");
            }
        }

        // ---------------------------------------------------------------
        // asmdef への反映
        // ---------------------------------------------------------------

        /// <summary>設定に合わせて全 asmdef を整える。変化が無ければ何も書かない。</summary>
        public static void SyncFromConfig()
        {
            HashSet<string> enabled = LoadEnabledIds();

            var touched = new List<string>();
            foreach (UtilPackage package in UtilsCatalog.Packages)
            {
                if (SyncOne(package, enabled.Contains(package.Id), out string path))
                    touched.Add(path);
            }

            if (touched.Count == 0) return;

            foreach (string path in touched)
                AssetDatabase.ImportAsset(path);
        }

        /// <summary>1 つの asmdef を整える。実際に書き換えたら true。</summary>
        private static bool SyncOne(UtilPackage package, bool enable, out string path)
        {
            path = null;
            try
            {
                path = AssetDatabase.GUIDToAssetPath(package.AsmdefGuid);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    // パッケージごと入っていない場合もある（分割配布・部分導入）。
                    path = null;
                    return false;
                }

                var asmdef = JsonUtility.FromJson<AsmdefJson>(File.ReadAllText(path));
                if (asmdef == null) return false;

                asmdef.defineConstraints ??= new List<string>();
                asmdef.versionDefines ??= new List<VersionDefine>();

                bool changed = false;

                // 制約は有効・無効にかかわらず常に置いておく。これが無いと
                // versionDefines を消しても素通りでコンパイルされてしまう。
                if (!asmdef.defineConstraints.Contains(package.Define))
                {
                    asmdef.defineConstraints.Add(package.Define);
                    changed = true;
                }

                int index = asmdef.versionDefines.FindIndex(x => x != null && x.define == package.Define);
                if (enable && index < 0)
                {
                    asmdef.versionDefines.Add(new VersionDefine(AlwaysTrueVersionDefineName, "", package.Define));
                    changed = true;
                }
                else if (!enable && index >= 0)
                {
                    asmdef.versionDefines.RemoveAt(index);
                    changed = true;
                }

                if (!changed)
                {
                    path = null;
                    return false;
                }

                File.WriteAllText(path, JsonUtility.ToJson(asmdef, true));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[YozoLab Utils] {package.DisplayName} の asmdef 更新に失敗しました: {e.Message}");
                path = null;
                return false;
            }
        }

        /// <summary>今この瞬間、asmdef 側で有効になっているか（設定ファイルではなく実状態）。</summary>
        public static bool IsCompiledIn(UtilPackage package)
        {
            try
            {
                string path = AssetDatabase.GUIDToAssetPath(package.AsmdefGuid);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

                var asmdef = JsonUtility.FromJson<AsmdefJson>(File.ReadAllText(path));
                return asmdef?.versionDefines != null
                    && asmdef.versionDefines.Any(x => x != null && x.define == package.Define);
            }
            catch
            {
                return false;
            }
        }

        // ---------------------------------------------------------------
        // asmdef の JSON 表現
        // ---------------------------------------------------------------

        // JsonUtility は宣言したフィールドしか往復させない。asmdef が持ちうる項目は
        // 全て並べておくこと。増えた項目を書き落とすと設定が消える。
        [Serializable]
        private sealed class AsmdefJson
        {
            public string name;
            public string rootNamespace;
            public List<string> references;
            public List<string> includePlatforms;
            public List<string> excludePlatforms;
            public bool allowUnsafeCode;
            public bool overrideReferences;
            public List<string> precompiledReferences;
            public bool autoReferenced;
            public List<string> defineConstraints;
            public List<VersionDefine> versionDefines;
            public bool noEngineReferences;
        }

        [Serializable]
        private sealed class VersionDefine
        {
            public string name;
            public string expression;
            public string define;

            public VersionDefine(string name, string expression, string define)
            {
                this.name = name;
                this.expression = expression;
                this.define = define;
            }
        }
    }
}
