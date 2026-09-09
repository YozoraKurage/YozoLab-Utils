using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// FBXファイルをHumanoidとしてインポートし、アニメーションクリップを抽出するエディタ拡張。
///
/// このファイルはウィンドウのライフサイクル・設定アセットの読み書き・多言語化のみを担当する。
/// 機能ごとの実装は以下の partial に分割している:
///   - FBXAnimationExtractorWindow.GUI.cs         … OnGUI / 各種 GUI 描画
///   - FBXAnimationExtractorWindow.Processing.cs  … FBX 抽出パイプライン (Execute / Refresh)
///   - FBXAnimationExtractorWindow.PostProcess.cs … カーブ後処理 / Generic 抽出 / シグネチャ
///   - FBXAnimationExtractorWindow.Template.cs    … Rule Detail のコピー / ペースト
/// </summary>
public partial class FBXAnimationExtractorWindow : EditorWindow
{
    protected FBXAnimationExtractorSettings settings;
    protected SerializedObject serializedSettings;
    protected SerializedProperty postProcessRulesProp;

    private bool showPostProcessRules = true;
    private Vector2 ruleListScrollPosition;
    private Vector2 ruleDetailScrollPosition;
    private int selectedRuleIndex = -1;
    private string ruleSearchText = string.Empty;
    private readonly HashSet<int> checkedRuleIndices = new HashSet<int>();

    // 設定は ProjectSettings/ に保存される（ScriptableSingleton）。パッケージ外なので VPM 更新で消えない。
    private const string SettingsFilePath = "ProjectSettings/FBXAnimationExtractorSettings.asset";

    // Rule Detailのコピー/ペースト用テンプレート（ドメイン内で1個）
    protected static RuleDetailTemplate ruleTemplate;

    [MenuItem("YozoLab/FBX Animation Extractor")]
    public static void ShowWindow()
    {
        GetWindow<FBXAnimationExtractorWindow>("FBX Animation Extractor");
    }

    private void OnEnable()
    {
        LoadOrCreateSettings();
    }

    private void OnDisable()
    {
        PersistSettings();
    }

    private void OnLostFocus()
    {
        PersistSettings();
    }

    private void LoadOrCreateSettings()
    {
        settings = FBXAnimationExtractorSettings.instance;

        // 旧バージョンはパッケージ内/Assets内の .asset に保存していた。
        // ProjectSettings 側にまだ実体が無い初回のみ、その内容を取り込む。
        MigrateLegacyAssetIfNeeded();

        MigrateGlobalDirectoriesIfNeeded();

        serializedSettings = new SerializedObject(settings);
        postProcessRulesProp = serializedSettings.FindProperty("postProcessRules");
    }

    /// <summary>
    /// 全フォルダ共通だった Target / Output Directory を、ルールフォルダごとの
    /// Source / Output Directory へ移す。
    ///
    /// 以前は全フォルダが共通の 1 組を使っていたので、移行では
    ///   - 未設定のフォルダすべてに旧共通値を入れる(今までと同じ挙動になる)
    ///   - フォルダに属さない Rule は行き場が無くなるので、専用フォルダを作って収容する
    /// の 2 つをやる。一度きり。
    /// </summary>
    private void MigrateGlobalDirectoriesIfNeeded()
    {
        if (settings == null || settings.perFolderDirectoriesMigrated)
        {
            return;
        }

        DefaultAsset legacySource = settings.targetDirectory;
        DefaultAsset legacyOutput = settings.outputDirectory;

        if (legacySource == null && legacyOutput == null)
        {
            // 移行するものが無い(新規プロジェクト)。印だけ付けて終わり。
            settings.perFolderDirectoriesMigrated = true;
            settings.SaveSettings();
            return;
        }

        settings.ruleFolders ??= new List<RuleFolderState>();
        settings.postProcessRules ??= new List<AnimationPostProcessRule>();

        foreach (RuleFolderState folder in settings.ruleFolders)
        {
            if (folder == null) continue;
            if (folder.sourceDirectory == null) folder.sourceDirectory = legacySource;
            if (folder.outputDirectory == null) folder.outputDirectory = legacyOutput;
        }

        // フォルダ無しの Rule の受け皿。Execute はフォルダ単位で走るようになったため、
        // ここへ入れておかないと今まで処理できていた Rule が黙って外れてしまう。
        bool hasFolderlessRule = settings.postProcessRules
            .Exists(rule => rule != null && string.IsNullOrWhiteSpace(rule.folder));
        if (hasFolderlessRule)
        {
            string name = MakeUniqueFolderName("Default");
            settings.ruleFolders.Add(new RuleFolderState
            {
                name = name,
                sourceDirectory = legacySource,
                outputDirectory = legacyOutput,
            });

            int moved = 0;
            foreach (AnimationPostProcessRule rule in settings.postProcessRules)
            {
                if (rule == null || !string.IsNullOrWhiteSpace(rule.folder)) continue;
                rule.folder = name;
                moved++;
            }
            Debug.Log($"[FBX Animation Extractor] Moved {moved} folderless rule(s) into the new folder \"{name}\".");
        }

        settings.targetDirectory = null;
        settings.outputDirectory = null;
        settings.perFolderDirectoriesMigrated = true;
        settings.SaveSettings();

        Debug.Log("[FBX Animation Extractor] Migrated the shared Target/Output Directory to per-folder Source/Output Directory.");
    }

    private string MakeUniqueFolderName(string desired)
    {
        bool Taken(string candidate) => settings.ruleFolders.Exists(
            f => f != null && string.Equals(f.name?.Trim(), candidate,
                                            System.StringComparison.OrdinalIgnoreCase));

        if (!Taken(desired)) return desired;
        for (int i = 2; ; i++)
        {
            string candidate = $"{desired} {i}";
            if (!Taken(candidate)) return candidate;
        }
    }

    /// <summary>編集中の内容を ProjectSettings/ のファイルへ確定保存する。</summary>
    protected void PersistSettings()
    {
        if (settings == null)
        {
            return;
        }

        serializedSettings?.ApplyModifiedProperties();
        settings.SaveSettings();
    }

    private void MigrateLegacyAssetIfNeeded()
    {
        // 既に ProjectSettings 側へ保存済みなら何もしない
        if (File.Exists(SettingsFilePath))
        {
            return;
        }

        // 既に内容を持っているなら上書きしない（安全側）
        bool alreadyHasData = settings.targetDirectory != null
            || settings.outputDirectory != null
            || (settings.ruleFolders != null && settings.ruleFolders.Count > 0)
            || (settings.postProcessRules != null && settings.postProcessRules.Count > 0);
        if (alreadyHasData)
        {
            return;
        }

        // プロジェクト内に残る旧 .asset を探し、最もルール数が多いものを採用する
        FBXAnimationExtractorSettings legacy = null;
        int legacyRuleCount = -1;
        foreach (string guid in AssetDatabase.FindAssets("t:FBXAnimationExtractorSettings"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var candidate = AssetDatabase.LoadAssetAtPath<FBXAnimationExtractorSettings>(path);
            if (candidate == null || candidate == settings)
            {
                continue;
            }

            int count = candidate.postProcessRules?.Count ?? 0;
            if (count > legacyRuleCount)
            {
                legacy = candidate;
                legacyRuleCount = count;
            }
        }

        if (legacy == null)
        {
            return;
        }

        // 旧アセットは共通ディレクトリしか持たない。ここでは素直に受け取り、
        // このあとの MigrateGlobalDirectoriesIfNeeded がフォルダ単位へ移す。
        settings.targetDirectory = legacy.targetDirectory;
        settings.outputDirectory = legacy.outputDirectory;
        settings.perFolderDirectoriesMigrated = false;
        settings.ruleFolders = legacy.ruleFolders != null
            ? new List<RuleFolderState>(legacy.ruleFolders)
            : new List<RuleFolderState>();
        settings.postProcessRules = legacy.postProcessRules != null
            ? new List<AnimationPostProcessRule>(legacy.postProcessRules)
            : new List<AnimationPostProcessRule>();
        settings.processCacheEntries = legacy.processCacheEntries != null
            ? new List<FbxProcessCacheEntry>(legacy.processCacheEntries)
            : new List<FbxProcessCacheEntry>();

        settings.SaveSettings();
        Debug.Log($"[FBX Animation Extractor] Migrated settings from legacy asset \"{AssetDatabase.GetAssetPath(legacy)}\" to {SettingsFilePath}.");
    }

    protected static class L10n
    {
        private const string PrefKey = "FBXAnimExtractor_Language";

        public static bool IsEnglish
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        public static string T(string jp, string en) => IsEnglish ? en : jp;
    }
}
