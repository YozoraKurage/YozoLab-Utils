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

    private const string SettingsFolderGuid = "815d1729ead52f34a847ad2e7a60ff91";
    private const string SettingsFileName = "FBXAnimationExtractorSettings.asset";
    private const string LegacySettingsPath = "Assets/Editor/FBXAnimationExtractorSettings.asset";

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

    private void LoadOrCreateSettings()
    {
        string settingsPath = GetSettingsPath();
        settings = AssetDatabase.LoadAssetAtPath<FBXAnimationExtractorSettings>(settingsPath);

        if (settings == null && settingsPath != LegacySettingsPath)
        {
            FBXAnimationExtractorSettings legacySettings = AssetDatabase.LoadAssetAtPath<FBXAnimationExtractorSettings>(LegacySettingsPath);
            if (legacySettings != null)
            {
                string settingsFolder = Path.GetDirectoryName(settingsPath)?.Replace("\\", "/");
                EnsureFolderExists(settingsFolder);

                string moveResult = AssetDatabase.MoveAsset(LegacySettingsPath, settingsPath);
                if (string.IsNullOrEmpty(moveResult))
                {
                    settings = AssetDatabase.LoadAssetAtPath<FBXAnimationExtractorSettings>(settingsPath);
                    Debug.Log($"[FBX Animation Extractor] Moved settings asset: {LegacySettingsPath} -> {settingsPath}");
                }
                else
                {
                    Debug.LogWarning($"[FBX Animation Extractor] Failed to move legacy settings asset: {moveResult}");
                }
            }
        }

        if (settings == null)
        {
            string settingsFolder = Path.GetDirectoryName(settingsPath)?.Replace("\\", "/");
            EnsureFolderExists(settingsFolder);

            settings = CreateInstance<FBXAnimationExtractorSettings>();
            AssetDatabase.CreateAsset(settings, settingsPath);
            AssetDatabase.SaveAssets();
        }

        serializedSettings = new SerializedObject(settings);
        targetDirectoryProp = serializedSettings.FindProperty("targetDirectory");
        outputDirectoryProp = serializedSettings.FindProperty("outputDirectory");
        postProcessRulesProp = serializedSettings.FindProperty("postProcessRules");
    }

    private static string GetSettingsPath()
    {
        string settingsFolderPath = AssetDatabase.GUIDToAssetPath(SettingsFolderGuid);
        if (string.IsNullOrEmpty(settingsFolderPath))
        {
            Debug.LogWarning("[FBX Animation Extractor] Settings folder GUID was not found. Falling back to Assets.");
            settingsFolderPath = "Assets";
        }

        return $"{settingsFolderPath}/{SettingsFileName}";
    }

    private static void EnsureFolderExists(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || folderPath == "Assets" || AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string[] segments = folderPath.Split('/');
        string currentPath = "Assets";

        for (int i = 1; i < segments.Length; i++)
        {
            string nextPath = $"{currentPath}/{segments[i]}";
            if (!AssetDatabase.IsValidFolder(nextPath))
            {
                AssetDatabase.CreateFolder(currentPath, segments[i]);
            }
            currentPath = nextPath;
        }
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
