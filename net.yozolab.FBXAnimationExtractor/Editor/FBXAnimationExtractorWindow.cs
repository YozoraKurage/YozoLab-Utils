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
    protected SerializedProperty targetDirectoryProp;
    protected SerializedProperty outputDirectoryProp;
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

        serializedSettings = new SerializedObject(settings);
        targetDirectoryProp = serializedSettings.FindProperty("targetDirectory");
        outputDirectoryProp = serializedSettings.FindProperty("outputDirectory");
        postProcessRulesProp = serializedSettings.FindProperty("postProcessRules");
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

        settings.targetDirectory = legacy.targetDirectory;
        settings.outputDirectory = legacy.outputDirectory;
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
