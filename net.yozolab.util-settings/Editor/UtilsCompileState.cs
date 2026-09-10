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
    /// パッケージを入れ直すと出荷時の状態（全て無効）へ戻ってしまうため、
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

        /// <summary>
        /// 有効なパッケージ Id の集合を読む。
        ///
        /// 設定ファイルに載っていないパッケージは、その asmdef の今の状態を既定として
        /// 採用し、設定ファイルへ書き足す。「載っていない = 無効」にしていたころは、
        /// パッケージを新しく追加するたびに出荷時の asmdef（有効）と設定ファイル
        /// （無効扱い）が食い違い、起動のたびに asmdef を書き換えては再コンパイルが
        /// 走っていた。
        /// </summary>
        public static HashSet<string> LoadEnabledIds()
        {
            HashSet<string> enabled = ReadConfig(out HashSet<string> known);

            if (HasUnlistedPackages(known))
            {
                SaveEnabledIds(enabled);
            }

            return enabled;
        }

        /// <summary>
        /// 設定ファイルを読む。行頭の "-" は明示的な無効。
        /// </summary>
        /// <param name="known">
        /// 有効・無効を問わず、設定ファイルが言及している Id。
        /// 「無効」と「まだ知らない」を区別するために要る。
        /// </param>
        private static HashSet<string> ReadConfig(out HashSet<string> known)
        {
            var enabled = new HashSet<string>();
            known = new HashSet<string>();

            try
            {
                if (!File.Exists(ConfigPath)) return enabled;

                foreach (string raw in File.ReadAllLines(ConfigPath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                    bool disabled = line.StartsWith("-", StringComparison.Ordinal);
                    string id = disabled ? line.Substring(1).Trim() : line;
                    if (id.Length == 0) continue;

                    known.Add(id);
                    if (!disabled) enabled.Add(id);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[YozoLab Utils] 設定の読み込みに失敗しました（asmdef の状態を既定にします）: {e.Message}");
            }

            return enabled;
        }

        /// <summary>
        /// 設定ファイルがまだ知らないパッケージがあるか。
        /// あれば設定ファイルを書き出す＝そのパッケージは無効として記録される。
        ///
        /// 出荷時の asmdef は「誰も切っていない = コンパイルされる」姿なので、
        /// アドオンだけを取り出せば単体で動く。一方このパッケージをまとめて
        /// 入れたときは、要るものだけを選んでもらう形にしたい。そこで、この
        /// 設定機構が初めて動いた時点で全部を無効側へ倒す。
        ///
        /// 代償は初回の 1 回だけ。設定ファイルは有効・無効を明示して書き出す
        /// ので、2 回目からは設定と asmdef が一致し、起動時の同期は何も書かない。
        /// 新しく増えたパッケージも同じで、初回に 1 度だけ切って以後は無音。
        /// </summary>
        private static bool HasUnlistedPackages(HashSet<string> known)
        {
            return UtilsCatalog.Packages.Any(package => !known.Contains(package.Id));
        }

        /// <summary>
        /// 設定を書き出す。カタログにある全パッケージを、無効なものは "-" 付きで
        /// 必ず 1 行ずつ書く。有効なものだけを書いていたころは「無効」と
        /// 「まだ知らない」が区別できなかった。
        /// </summary>
        public static void SaveEnabledIds(HashSet<string> enabled)
        {
            try
            {
                Directory.CreateDirectory(SettingsFolder);

                var lines = new List<string>
                {
                    "# YozoLab Utils: コンパイルするパッケージの一覧。",
                    "# このファイルが正本。asmdef 側はここから復元される。",
                    "# 有効にするには行頭の - を消すか、YozoLab/Utils Settings で選ぶ。",
                    "# 行頭の - は無効。ここに無いパッケージは無効として書き足される。",
                };
                lines.AddRange(UtilsCatalog.Packages.Select(
                    p => enabled.Contains(p.Id) ? p.Id : "-" + p.Id));

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
                bool enable = enabled.Contains(package.Id);

                if (SyncOne(package, package.AsmdefGuid, enable, out string path))
                    touched.Add(path);

                // 付随するアセンブリ(テスト、必須パッケージ未導入時の案内)も
                // 本体と同じ条件で開け閉めする。本体を切ったのに案内だけ残る、
                // 本体を戻したのにテストが死んだまま、といった食い違いを防ぐ。
                foreach (string companion in package.CompanionAsmdefGuids)
                {
                    if (!string.IsNullOrEmpty(companion)
                        && SyncOne(package, companion, enable, out string companionPath))
                        touched.Add(companionPath);
                }
            }

            if (touched.Count == 0) return;

            foreach (string path in touched)
                AssetDatabase.ImportAsset(path);
        }

        /// <summary>
        /// asmdef を 1 つ整える。実際に書き換えたら true。
        ///
        /// 対象は <paramref name="asmdefGuid"/> で指す。同じパッケージの
        /// 本体とテストの両方に、同じ制約とシンボルを書き込むために分けてある。
        /// </summary>
        private static bool SyncOne(UtilPackage package, string asmdefGuid, bool enable, out string path)
        {
            path = null;
            try
            {
                path = AssetDatabase.GUIDToAssetPath(asmdefGuid);
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

                // 制約は否定形で常に置いておく。誰も Define を立てなければ
                // コンパイルされる、が出荷時の姿。これが無いと、無効化の
                // シンボルを足しても素通りでコンパイルされてしまう。
                string negated = "!" + package.Define;
                if (!asmdef.defineConstraints.Contains(negated))
                {
                    asmdef.defineConstraints.Add(negated);
                    changed = true;
                }

                // 無効化は versionDefines に常に真の 1 件を足すこと。有効化は
                // それを消して出荷時の姿へ戻すこと。外部パッケージ必須の条件は
                // asmdef に常設されていて、ここでは触らない。
                int index = asmdef.versionDefines.FindIndex(x => x != null && x.define == package.Define);
                if (!enable && index < 0)
                {
                    asmdef.versionDefines.Add(
                        new VersionDefine(AlwaysTrueVersionDefineName, "", package.Define));
                    changed = true;
                }
                else if (enable && index >= 0)
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

        /// <summary>今この瞬間、asmdef 側で有効になっているか（設定ファイルではなく実状態）。
        /// 無効化のシンボルが立っていなければ有効。</summary>
        public static bool IsCompiledIn(UtilPackage package)
        {
            try
            {
                string path = AssetDatabase.GUIDToAssetPath(package.AsmdefGuid);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

                var asmdef = JsonUtility.FromJson<AsmdefJson>(File.ReadAllText(path));
                return asmdef?.versionDefines == null
                    || !asmdef.versionDefines.Any(x => x != null && x.define == package.Define);
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
