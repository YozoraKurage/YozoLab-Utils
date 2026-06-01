using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// FBXAnimationExtractorWindow の派生版。Target Directory 配下の FBX を走査して、
/// 未登録のものを Post Process Rule として自動追加するボタンを Rule ツールバーに追加する。
/// </summary>
public class FBXAnimationExtractorAutoCollectWindow : FBXAnimationExtractorWindow
{
    [MenuItem("YozoLab/FBX Animation Extractor (Auto Collect)")]
    public static void ShowAutoCollectWindow()
    {
        GetWindow<FBXAnimationExtractorAutoCollectWindow>("FBX Auto Collect");
    }

    protected override void DrawRuleToolbarExtras()
    {
        using (new EditorGUI.DisabledScope(settings == null || settings.targetDirectory == null))
        {
            if (GUILayout.Button(new GUIContent("Auto Collect",
                    "Target Directory以下のFBXを走査し、未登録の名前を Rule として追加します"),
                GUILayout.Width(110)))
            {
                AutoCollectFromTargetDirectory();
            }
        }
    }

    private void AutoCollectFromTargetDirectory()
    {
        if (settings == null || settings.targetDirectory == null)
        {
            Debug.LogWarning("[FBX Animation Extractor] Target Directory is not set.");
            return;
        }

        string targetPath = AssetDatabase.GetAssetPath(settings.targetDirectory);
        if (string.IsNullOrEmpty(targetPath) || !AssetDatabase.IsValidFolder(targetPath))
        {
            Debug.LogWarning("[FBX Animation Extractor] Target Directory is invalid.");
            return;
        }

        string[] fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { targetPath });
        List<string> fbxNames = fbxGuids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.ToLower().EndsWith(".fbx"))
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (fbxNames.Count == 0)
        {
            Debug.LogWarning($"[FBX Animation Extractor] No FBX files found under \"{targetPath}\".");
            return;
        }

        serializedSettings.ApplyModifiedProperties();
        Undo.RecordObject(settings, "Auto Collect Rules from Target");

        var existingNames = new HashSet<string>(
            settings.postProcessRules
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.targetName))
                .Select(r => r.targetName.Trim().ToLowerInvariant()));

        int added = 0;
        foreach (string name in fbxNames)
        {
            string key = name.Trim().ToLowerInvariant();
            if (existingNames.Contains(key)) continue;

            settings.postProcessRules.Add(new AnimationPostProcessRule { targetName = name });
            existingNames.Add(key);
            added++;
        }

        EditorUtility.SetDirty(settings);
        serializedSettings.Update();

        Debug.Log($"[FBX Animation Extractor] Auto Collect: scanned {fbxNames.Count} FBX, added {added} new rule(s).");
    }
}
