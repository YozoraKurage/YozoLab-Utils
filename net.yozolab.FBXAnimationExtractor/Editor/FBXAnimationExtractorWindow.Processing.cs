using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// FBXAnimationExtractorWindow の FBX 抽出パイプライン担当。
/// Execute(ProcessFBXFiles) と Refresh(RefreshOutputAnimations)、および差分スキップ用キャッシュをここにまとめる。
/// </summary>
public partial class FBXAnimationExtractorWindow
{
    private void ProcessFBXFiles()
    {
        string targetPath = AssetDatabase.GetAssetPath(settings.targetDirectory);
        string outputPath = AssetDatabase.GetAssetPath(settings.outputDirectory);

        // FBXファイルを取得
        string[] fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { targetPath });
        string[] fbxPaths = fbxGuids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.ToLower().EndsWith(".fbx"))
            .ToArray();

        if (fbxPaths.Length == 0)
        {
            Debug.LogWarning("[FBX Animation Extractor] No FBX files found in the specified folder.");
            return;
        }

        Debug.Log($"[FBX Animation Extractor] Processing started: {fbxPaths.Length} FBX file(s)");
        int processedCount = 0;
        int skippedCount = 0;

        try
        {
            for (int i = 0; i < fbxPaths.Length; i++)
            {
                string fbxPath = fbxPaths[i];
                string fbxName = Path.GetFileNameWithoutExtension(fbxPath);
                AnimationPostProcessRule matchingRule = FindMatchingRule(fbxName);
                string sourceDependencyHash;
                string ruleSignature;

                EditorUtility.DisplayProgressBar("FBX Animation Extractor",
                    $"Processing: {fbxName} ({i + 1}/{fbxPaths.Length})",
                    (float)i / fbxPaths.Length);

                if (ShouldSkipProcessing(fbxPath, fbxName, outputPath, matchingRule, out sourceDependencyHash, out ruleSignature))
                {
                    skippedCount++;
                    Debug.Log($"[FBX Animation Extractor] No changes, skipped: {fbxName}");
                    continue;
                }

                // FBXのインポート設定を変更
                ConfigureModelImporter(fbxPath, matchingRule);

                // アニメーションクリップを抽出・保存
                bool extracted = ExtractAndSaveAnimationClip(fbxPath, outputPath, fbxName);
                if (!extracted)
                {
                    continue;
                }

                string generatedClipPath = $"{outputPath}/{fbxName}.anim";
                UpdateProcessCache(fbxPath, sourceDependencyHash, ruleSignature, generatedClipPath);
                processedCount++;

                Debug.Log($"[FBX Animation Extractor] Processed: {fbxName}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[FBX Animation Extractor] All done: total={fbxPaths.Length}, processed={processedCount}, skipped={skippedCount}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void ConfigureModelImporter(string fbxPath, AnimationPostProcessRule matchingRule)
    {
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogWarning($"[FBX Animation Extractor] Failed to get ModelImporter for: {fbxPath}");
            return;
        }

        // Humanoidとして設定
        importer.animationType = ModelImporterAnimationType.Human;

        // Avatar設定
        if (matchingRule != null && matchingRule.useOtherAvatarDefinition)
        {
            if (matchingRule.avatarDefinition != null)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = matchingRule.avatarDefinition;
            }
            else
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.sourceAvatar = null;
                Debug.LogWarning($"[FBX Animation Extractor] 'Use other avatar definition' is enabled but Avatar is not set. Fallback to CreateFromThisModel: {fbxPath}");
            }
        }
        else
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;
        }

        // クリップのアニメーション設定を更新
        ModelImporterClipAnimation[] clipAnimations = importer.clipAnimations;
        ModelImporterClipAnimation[] defaultClipAnimations = importer.defaultClipAnimations;

        // clipAnimationsが空の場合はdefaultClipAnimationsを使用
        if (clipAnimations == null || clipAnimations.Length == 0)
        {
            clipAnimations = defaultClipAnimations;
        }

        // FBX上書き時に古いStart/Endが残らないよう、毎回デフォルト長(最大)へ戻す
        Dictionary<string, ModelImporterClipAnimation> defaultClipMap = new Dictionary<string, ModelImporterClipAnimation>();
        if (defaultClipAnimations != null)
        {
            for (int i = 0; i < defaultClipAnimations.Length; i++)
            {
                ModelImporterClipAnimation defaultClip = defaultClipAnimations[i];
                if (!string.IsNullOrEmpty(defaultClip.name) && !defaultClipMap.ContainsKey(defaultClip.name))
                {
                    defaultClipMap.Add(defaultClip.name, defaultClip);
                }
            }
        }

        for (int i = 0; i < clipAnimations.Length; i++)
        {
            ModelImporterClipAnimation clip = clipAnimations[i];

            if (!string.IsNullOrEmpty(clip.name) && defaultClipMap.TryGetValue(clip.name, out ModelImporterClipAnimation defaultClipRange))
            {
                clip.firstFrame = defaultClipRange.firstFrame;
                clip.lastFrame = defaultClipRange.lastFrame;
            }
            else if (defaultClipAnimations != null && i < defaultClipAnimations.Length)
            {
                clip.firstFrame = defaultClipAnimations[i].firstFrame;
                clip.lastFrame = defaultClipAnimations[i].lastFrame;
            }

            // Root Transform Rotation
            clip.lockRootRotation = true;
            clip.keepOriginalOrientation = true;

            // Root Transform Position (Y)
            clip.lockRootHeightY = true;
            clip.keepOriginalPositionY = true;

            // Root Transform Position (XZ)
            clip.lockRootPositionXZ = true;
            clip.keepOriginalPositionXZ = true;

            clipAnimations[i] = clip;
        }

        importer.clipAnimations = clipAnimations;

        // 設定を保存して再インポート
        importer.SaveAndReimport();
    }

    private bool ExtractAndSaveAnimationClip(string fbxPath, string outputPath, string fbxName)
    {
        // FBXからアニメーションクリップを取得
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        AnimationClip sourceClip = assets
            .OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        if (sourceClip == null)
        {
            Debug.LogWarning($"[FBX Animation Extractor] No animation clip found in: {fbxPath}");
            return false;
        }

        string outputFilePath = $"{outputPath}/{fbxName}.anim";
        AnimationClip existingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputFilePath);

        // 抽出処理はメモリ上の一時クリップで行う
        AnimationClip workingClip = new AnimationClip();
        try
        {
            EditorUtility.CopySerialized(sourceClip, workingClip);
            workingClip.name = fbxName;

            ApplyPostProcessRules(workingClip, fbxName, fbxPath);

            if (existingClip != null)
            {
                // 既存ファイルがある場合: メタデータを保存してから中身を置き換え(GUID保持)
                AnimationClipSettings existingSettings = AnimationUtility.GetAnimationClipSettings(existingClip);
                CopyAnimationCurves(workingClip, existingClip);
                AnimationUtility.SetAnimationClipSettings(existingClip, existingSettings);
                EditorUtility.SetDirty(existingClip);
            }
            else
            {
                // 新規ファイルの場合: 後処理済みクリップを資産として書き出し
                AnimationClip newClip = new AnimationClip();
                EditorUtility.CopySerialized(workingClip, newClip);
                newClip.name = fbxName;
                AssetDatabase.CreateAsset(newClip, outputFilePath);
            }
        }
        finally
        {
            if (workingClip != null) DestroyImmediate(workingClip);
        }

        return true;
    }

    private void CopyAnimationCurves(AnimationClip sourceClip, AnimationClip destinationClip)
    {
        destinationClip.ClearCurves();

        EditorCurveBinding[] curveBindings = AnimationUtility.GetCurveBindings(sourceClip);
        foreach (EditorCurveBinding binding in curveBindings)
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(sourceClip, binding);
            AnimationUtility.SetEditorCurve(destinationClip, binding, curve);
        }

        EditorCurveBinding[] objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(sourceClip);
        foreach (EditorCurveBinding binding in objectBindings)
        {
            ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(sourceClip, binding);
            AnimationUtility.SetObjectReferenceCurve(destinationClip, binding, keyframes);
        }

        AnimationUtility.SetAnimationEvents(destinationClip, AnimationUtility.GetAnimationEvents(sourceClip));
    }

    private bool ShouldSkipProcessing(string fbxPath, string fbxName, string outputPath, AnimationPostProcessRule matchingRule, out string sourceDependencyHash, out string ruleSignature)
    {
        sourceDependencyHash = AssetDatabase.GetAssetDependencyHash(fbxPath).ToString();
        ruleSignature = BuildRuleSignature(matchingRule);

        string outputFilePath = $"{outputPath}/{fbxName}.anim";
        AnimationClip outputClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputFilePath);
        if (outputClip == null)
        {
            return false;
        }

        FbxProcessCacheEntry cacheEntry = GetProcessCacheEntry(fbxPath);
        if (cacheEntry == null)
        {
            return false;
        }

        return string.Equals(cacheEntry.sourceDependencyHash, sourceDependencyHash, StringComparison.Ordinal)
            && string.Equals(cacheEntry.ruleSignature, ruleSignature, StringComparison.Ordinal);
    }

    private void UpdateProcessCache(string fbxPath, string sourceDependencyHash, string ruleSignature, string generatedClipPath)
    {
        if (settings.processCacheEntries == null)
        {
            settings.processCacheEntries = new List<FbxProcessCacheEntry>();
        }

        FbxProcessCacheEntry cacheEntry = GetProcessCacheEntry(fbxPath);
        if (cacheEntry == null)
        {
            cacheEntry = new FbxProcessCacheEntry();
            settings.processCacheEntries.Add(cacheEntry);
        }

        cacheEntry.fbxAssetPath = fbxPath;
        cacheEntry.sourceDependencyHash = sourceDependencyHash;
        cacheEntry.ruleSignature = ruleSignature;
        cacheEntry.generatedClipAssetPath = generatedClipPath;
        EditorUtility.SetDirty(settings);
    }

    private FbxProcessCacheEntry GetProcessCacheEntry(string fbxPath)
    {
        if (settings.processCacheEntries == null)
        {
            settings.processCacheEntries = new List<FbxProcessCacheEntry>();
        }

        return settings.processCacheEntries.FirstOrDefault(
            entry => entry != null && string.Equals(entry.fbxAssetPath, fbxPath, StringComparison.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════
    //  Refresh: Output Directory以下の .anim を GUID 維持で空アニメ化
    // ═══════════════════════════════════════════════════════════════
    private void RefreshOutputAnimations()
    {
        if (outputDirectoryProp == null || outputDirectoryProp.objectReferenceValue == null)
        {
            Debug.LogWarning("[FBX Animation Extractor] Output Directory is not set.");
            return;
        }

        string outputPath = AssetDatabase.GetAssetPath(outputDirectoryProp.objectReferenceValue);
        if (string.IsNullOrEmpty(outputPath) || !AssetDatabase.IsValidFolder(outputPath))
        {
            Debug.LogWarning("[FBX Animation Extractor] Output Directory is invalid.");
            return;
        }

        bool confirmed = EditorUtility.DisplayDialog(
            L10n.T("Refresh", "Refresh"),
            L10n.T(
                $"\"{outputPath}\" 配下のすべての .anim のカーブ/イベントを削除し、空アニメに戻します。\nGUIDは保持されます。実行しますか?",
                $"Clear all curves/events of every .anim under \"{outputPath}\" and reduce them to empty animations.\nGUIDs will be preserved. Continue?"),
            L10n.T("実行", "OK"),
            L10n.T("キャンセル", "Cancel"));

        if (!confirmed) return;

        string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { outputPath });
        int count = 0;

        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null) continue;

                EditorUtility.DisplayProgressBar("FBX Animation Extractor",
                    $"Clearing: {clip.name} ({i + 1}/{guids.Length})",
                    (float)i / Mathf.Max(1, guids.Length));

                clip.ClearCurves();
                AnimationUtility.SetAnimationEvents(clip, new AnimationEvent[0]);

                EditorCurveBinding[] objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                foreach (EditorCurveBinding b in objectBindings)
                {
                    AnimationUtility.SetObjectReferenceCurve(clip, b, null);
                }

                EditorUtility.SetDirty(clip);
                count++;
            }

            // 空にしたClipは次回Executeで再生成されるよう、対応するキャッシュを無効化
            if (settings.processCacheEntries != null)
            {
                foreach (FbxProcessCacheEntry entry in settings.processCacheEntries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.generatedClipAssetPath)) continue;
                    if (entry.generatedClipAssetPath.StartsWith(outputPath, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.sourceDependencyHash = string.Empty;
                        entry.ruleSignature = string.Empty;
                    }
                }
                EditorUtility.SetDirty(settings);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[FBX Animation Extractor] Refresh: cleared {count} clip(s) under \"{outputPath}\".");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }
}
