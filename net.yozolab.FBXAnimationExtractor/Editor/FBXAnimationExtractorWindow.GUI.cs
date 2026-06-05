using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// FBXAnimationExtractorWindow の GUI 描画担当。OnGUI から各ペインの描画までをここにまとめる。
/// </summary>
public partial class FBXAnimationExtractorWindow
{
    private void OnGUI()
    {
        // シーン再生/停止を挟むと ScriptableSingleton が破棄・再生成され、
        // 既存の SerializedObject / SerializedProperty はターゲット破棄済みで無効になる。
        // serializedSettings 自体は null にならないため、targetObject の破棄を検知して再ロードする。
        if (serializedSettings == null || serializedSettings.targetObject == null || settings == null)
        {
            LoadOrCreateSettings();
        }

        serializedSettings.Update();

        // ── 上半分: ヘッダ + ディレクトリ + Rulesセクション(可変高) ──
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("FBX Animation Extractor", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(L10n.IsEnglish ? "EN" : "JP", GUILayout.Width(35)))
        {
            L10n.IsEnglish = !L10n.IsEnglish;
            Repaint();
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(targetDirectoryProp, new GUIContent("Target Directory", L10n.T("処理対象のFBXが含まれるフォルダ", "Folder containing the FBX files to process")));
        EditorGUILayout.PropertyField(outputDirectoryProp, new GUIContent("Output Directory", L10n.T("アニメーションファイルの保存先フォルダ", "Output folder for extracted animation clips")));

        if (targetDirectoryProp.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox(L10n.T("Target Directoryを設定してください。", "Please set Target Directory."), MessageType.Warning);
        }
        if (outputDirectoryProp.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox(L10n.T("Output Directoryを設定してください。", "Please set Output Directory."), MessageType.Warning);
        }

        EditorGUILayout.Space();

        // 後処理ルールセクション(残り高さを使う)
        showPostProcessRules = EditorGUILayout.Foldout(showPostProcessRules, "Post Process Rules", true);
        if (showPostProcessRules)
        {
            DrawPostProcessRulesSection();
        }
        else
        {
            GUILayout.FlexibleSpace();
        }

        // ── 下端: Execute / Refresh は常に最下部 ──
        DrawExecuteBar();

        serializedSettings.ApplyModifiedProperties();
    }

    private void DrawExecuteBar()
    {
        bool isValid = targetDirectoryProp.objectReferenceValue != null
                    && outputDirectoryProp.objectReferenceValue != null;
        bool canRefresh = outputDirectoryProp.objectReferenceValue != null;

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();

        using (new EditorGUI.DisabledScope(!isValid))
        {
            if (GUILayout.Button("Execute", GUILayout.Height(34)))
            {
                serializedSettings.ApplyModifiedProperties();
                ProcessFBXFiles();
            }
        }

        using (new EditorGUI.DisabledScope(!isValid))
        {
            if (GUILayout.Button(new GUIContent("Re-export All",
                    L10n.T("差分キャッシュを無視し、対象フォルダの全FBXを強制的に再エクスポートします",
                           "Ignore the diff cache and force a full re-export of every FBX in the target folder")),
                GUILayout.Height(34), GUILayout.Width(140)))
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    L10n.T("全再エクスポート", "Re-export All"),
                    L10n.T(
                        "キャッシュを無視して対象フォルダの全FBXを再エクスポートします。\nファイル数が多いと時間がかかります。実行しますか?",
                        "Ignore the cache and re-export every FBX in the target folder.\nThis can take a while for large folders. Continue?"),
                    L10n.T("実行", "OK"),
                    L10n.T("キャンセル", "Cancel"));

                if (confirmed)
                {
                    serializedSettings.ApplyModifiedProperties();
                    ProcessFBXFiles(true);
                }
            }
        }

        using (new EditorGUI.DisabledScope(!canRefresh))
        {
            if (GUILayout.Button(new GUIContent("Refresh",
                    L10n.T("Output Directory以下の全 .anim のカーブを削除して空アニメに戻します(GUIDは維持)",
                           "Clear all curves of every .anim under Output Directory (GUIDs preserved)")),
                GUILayout.Height(34), GUILayout.Width(140)))
            {
                serializedSettings.ApplyModifiedProperties();
                RefreshOutputAnimations();
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawPostProcessRulesSection()
    {
        EditorGUILayout.BeginVertical("box", GUILayout.ExpandHeight(true));
        EnsureSelectedRuleIndex();
        DrawRuleToolbar();
        DrawTemplateToolbar();

        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
        DrawRuleListPane();
        DrawRuleDetailPane();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawRuleToolbar()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Rules: {postProcessRulesProp.arraySize}", EditorStyles.boldLabel, GUILayout.Width(90));
        GUILayout.FlexibleSpace();

        GUILayout.Label("Search", GUILayout.Width(45));
        ruleSearchText = EditorGUILayout.TextField(ruleSearchText, GUILayout.Width(180));

        if (GUILayout.Button("Add", GUILayout.Width(60)))
        {
            int newIndex = postProcessRulesProp.arraySize;
            postProcessRulesProp.InsertArrayElementAtIndex(newIndex);
            InitializeRule(postProcessRulesProp.GetArrayElementAtIndex(newIndex));
            selectedRuleIndex = newIndex;
        }

        using (new EditorGUI.DisabledScope(selectedRuleIndex < 0 || selectedRuleIndex >= postProcessRulesProp.arraySize))
        {
            if (GUILayout.Button("Duplicate", GUILayout.Width(80)))
            {
                int duplicateIndex = selectedRuleIndex;
                postProcessRulesProp.InsertArrayElementAtIndex(duplicateIndex);
                selectedRuleIndex = duplicateIndex + 1;

                SerializedProperty nameProp = postProcessRulesProp
                    .GetArrayElementAtIndex(selectedRuleIndex)
                    .FindPropertyRelative("targetName");

                if (!string.IsNullOrWhiteSpace(nameProp.stringValue))
                {
                    nameProp.stringValue = $"{nameProp.stringValue}_copy";
                }
            }

            if (GUILayout.Button("Delete", GUILayout.Width(70)))
            {
                postProcessRulesProp.DeleteArrayElementAtIndex(selectedRuleIndex);
                checkedRuleIndices.Clear();
                selectedRuleIndex = Mathf.Clamp(selectedRuleIndex, 0, postProcessRulesProp.arraySize - 1);
            }
        }

        DrawAutoCollectButton();

        EditorGUILayout.EndHorizontal();
    }

    private void DrawAutoCollectButton()
    {
        using (new EditorGUI.DisabledScope(settings == null || settings.targetDirectory == null))
        {
            if (GUILayout.Button(new GUIContent("Auto Collect",
                    L10n.T("Target Directory以下のFBXを走査し、未登録の名前を Rule として追加します",
                           "Scan FBX under Target Directory and add unregistered names as Rules")),
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
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
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
        settings.SaveSettings();

        Debug.Log($"[FBX Animation Extractor] Auto Collect: scanned {fbxNames.Count} FBX, added {added} new rule(s).");
    }

    private void DrawTemplateToolbar()
    {
        EditorGUILayout.BeginHorizontal();

        string source = ruleTemplate == null
            ? L10n.T("(未設定)", "(empty)")
            : $"\"{ruleTemplate.sourceTargetName}\"";
        EditorGUILayout.LabelField(new GUIContent(
            $"Template: {source}",
            L10n.T("選択中Ruleの内容をテンプレートとしてコピーし、複数のRuleに貼り付けできます",
                   "Copy the selected rule's details as a template and paste to multiple rules")),
            EditorStyles.miniBoldLabel);

        GUILayout.FlexibleSpace();

        using (new EditorGUI.DisabledScope(selectedRuleIndex < 0 || selectedRuleIndex >= postProcessRulesProp.arraySize))
        {
            if (GUILayout.Button(new GUIContent("Copy Template",
                    L10n.T("選択中RuleからテンプレートをコピーTarget Name除く)",
                           "Capture the selected rule as a template (excluding Target Name)")),
                GUILayout.Width(130)))
            {
                CopySelectedRuleToTemplate();
            }
        }

        List<int> pasteTargets = GetPasteTargetIndices();
        using (new EditorGUI.DisabledScope(ruleTemplate == null || pasteTargets.Count == 0))
        {
            if (GUILayout.Button(new GUIContent(
                    $"Paste ({pasteTargets.Count})",
                    L10n.T("チェック済みRuleにペースト(未チェックなら選択中Rule)",
                           "Paste to checked rules (or the selected rule when none are checked)")),
                GUILayout.Width(110)))
            {
                PasteTemplateToRules(pasteTargets);
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private List<int> GetPasteTargetIndices()
    {
        var list = new List<int>();
        if (postProcessRulesProp.arraySize == 0) return list;

        if (checkedRuleIndices.Count > 0)
        {
            foreach (int i in checkedRuleIndices)
            {
                if (i >= 0 && i < postProcessRulesProp.arraySize) list.Add(i);
            }
        }
        else if (selectedRuleIndex >= 0 && selectedRuleIndex < postProcessRulesProp.arraySize)
        {
            list.Add(selectedRuleIndex);
        }
        list.Sort();
        return list;
    }

    private void DrawRuleListPane()
    {
        EditorGUILayout.BeginVertical("box", GUILayout.Width(360), GUILayout.MinHeight(400), GUILayout.ExpandHeight(true));

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Rule List", EditorStyles.miniBoldLabel);
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField($"Checked: {checkedRuleIndices.Count}", EditorStyles.miniLabel, GUILayout.Width(90));
        EditorGUILayout.EndHorizontal();

        if (postProcessRulesProp.arraySize == 0)
        {
            EditorGUILayout.HelpBox(L10n.T("ルールが未設定です。Addで追加してください。", "No rules configured. Click Add to create one."), MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        ruleListScrollPosition = EditorGUILayout.BeginScrollView(ruleListScrollPosition);

        string normalizedSearch = string.IsNullOrWhiteSpace(ruleSearchText)
            ? string.Empty
            : ruleSearchText.Trim().ToLowerInvariant();

        for (int i = 0; i < postProcessRulesProp.arraySize; i++)
        {
            SerializedProperty ruleProp = postProcessRulesProp.GetArrayElementAtIndex(i);
            SerializedProperty targetNameProp = ruleProp.FindPropertyRelative("targetName");

            string targetName = string.IsNullOrWhiteSpace(targetNameProp.stringValue)
                ? "(No Target Name)"
                : targetNameProp.stringValue.Trim();

            if (!string.IsNullOrEmpty(normalizedSearch)
                && targetName.ToLowerInvariant().IndexOf(normalizedSearch, StringComparison.Ordinal) < 0)
            {
                continue;
            }

            bool isSelected = selectedRuleIndex == i;
            Color prevBg = GUI.backgroundColor;
            if (isSelected)
            {
                GUI.backgroundColor = new Color(0.35f, 0.58f, 0.85f, 0.9f);
            }

            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();
            bool wasChecked = checkedRuleIndices.Contains(i);
            bool isChecked = EditorGUILayout.Toggle(wasChecked, GUILayout.Width(18));
            if (isChecked != wasChecked)
            {
                if (isChecked) checkedRuleIndices.Add(i);
                else checkedRuleIndices.Remove(i);
            }
            if (GUILayout.Button($"{i + 1}. {targetName}", EditorStyles.miniButton, GUILayout.Height(22)))
            {
                selectedRuleIndex = i;
            }
            EditorGUILayout.EndHorizontal();

            GUI.backgroundColor = prevBg;

            DrawGeneratedClipShortcut(targetNameProp.stringValue);
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();

        using (new EditorGUI.DisabledScope(selectedRuleIndex <= 0 || selectedRuleIndex >= postProcessRulesProp.arraySize))
        {
            if (GUILayout.Button("Move Up"))
            {
                postProcessRulesProp.MoveArrayElement(selectedRuleIndex, selectedRuleIndex - 1);
                selectedRuleIndex--;
                checkedRuleIndices.Clear();
            }
        }

        using (new EditorGUI.DisabledScope(selectedRuleIndex < 0 || selectedRuleIndex >= postProcessRulesProp.arraySize - 1))
        {
            if (GUILayout.Button("Move Down"))
            {
                postProcessRulesProp.MoveArrayElement(selectedRuleIndex, selectedRuleIndex + 1);
                selectedRuleIndex++;
                checkedRuleIndices.Clear();
            }
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Check All Filtered", EditorStyles.miniButton))
        {
            CheckAllFiltered(normalizedSearch);
        }
        if (GUILayout.Button("Uncheck All", EditorStyles.miniButton))
        {
            checkedRuleIndices.Clear();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void CheckAllFiltered(string normalizedSearch)
    {
        for (int i = 0; i < postProcessRulesProp.arraySize; i++)
        {
            if (!string.IsNullOrEmpty(normalizedSearch))
            {
                SerializedProperty ruleProp = postProcessRulesProp.GetArrayElementAtIndex(i);
                string name = ruleProp.FindPropertyRelative("targetName").stringValue ?? string.Empty;
                if (name.Trim().ToLowerInvariant().IndexOf(normalizedSearch, StringComparison.Ordinal) < 0)
                {
                    continue;
                }
            }
            checkedRuleIndices.Add(i);
        }
    }

    private void DrawGeneratedClipShortcut(string targetName)
    {
        AnimationClip generatedClip = ResolveGeneratedClipForRule(targetName);
        AnimationClip generatedGenericClip = ResolveGeneratedGenericClipForRule(targetName);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("Clip", generatedClip, typeof(AnimationClip), false);
            if (generatedGenericClip != null)
            {
                EditorGUILayout.ObjectField("Generic", generatedGenericClip, typeof(AnimationClip), false);
            }
        }
    }

    private AnimationClip ResolveGeneratedGenericClipForRule(string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName) || settings.processCacheEntries == null)
        {
            return null;
        }

        string normalizedTargetName = targetName.Trim();
        foreach (FbxProcessCacheEntry cacheEntry in settings.processCacheEntries)
        {
            if (cacheEntry == null || string.IsNullOrEmpty(cacheEntry.fbxAssetPath))
                continue;

            string fbxName = Path.GetFileNameWithoutExtension(cacheEntry.fbxAssetPath);
            if (!string.Equals(fbxName, normalizedTargetName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(cacheEntry.generatedGenericClipAssetPath))
            {
                return AssetDatabase.LoadAssetAtPath<AnimationClip>(cacheEntry.generatedGenericClipAssetPath);
            }
        }

        return null;
    }

    private AnimationClip ResolveGeneratedClipForRule(string targetName)
    {
        string clipPath = ResolveGeneratedClipPathForRule(targetName);
        if (string.IsNullOrEmpty(clipPath))
        {
            return null;
        }

        return AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
    }

    private string ResolveGeneratedClipPathForRule(string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return string.Empty;
        }

        string normalizedTargetName = targetName.Trim();

        if (settings.processCacheEntries != null)
        {
            foreach (FbxProcessCacheEntry cacheEntry in settings.processCacheEntries)
            {
                if (cacheEntry == null || string.IsNullOrEmpty(cacheEntry.fbxAssetPath))
                {
                    continue;
                }

                string fbxName = Path.GetFileNameWithoutExtension(cacheEntry.fbxAssetPath);
                if (!string.Equals(fbxName, normalizedTargetName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(cacheEntry.generatedClipAssetPath)
                    && AssetDatabase.LoadAssetAtPath<AnimationClip>(cacheEntry.generatedClipAssetPath) != null)
                {
                    return cacheEntry.generatedClipAssetPath;
                }
            }
        }

        if (outputDirectoryProp == null || outputDirectoryProp.objectReferenceValue == null)
        {
            return string.Empty;
        }

        string outputPath = AssetDatabase.GetAssetPath(outputDirectoryProp.objectReferenceValue);
        if (string.IsNullOrEmpty(outputPath))
        {
            return string.Empty;
        }

        string fallbackClipPath = $"{outputPath}/{normalizedTargetName}.anim";
        return AssetDatabase.LoadAssetAtPath<AnimationClip>(fallbackClipPath) != null
            ? fallbackClipPath
            : string.Empty;
    }

    private void DrawRuleDetailPane()
    {
        EditorGUILayout.BeginVertical("box", GUILayout.MinHeight(400), GUILayout.ExpandHeight(true));
        EditorGUILayout.LabelField("Rule Detail", EditorStyles.miniBoldLabel);

        if (selectedRuleIndex < 0 || selectedRuleIndex >= postProcessRulesProp.arraySize)
        {
            EditorGUILayout.HelpBox(L10n.T("左側のRule Listから編集対象を選択してください。", "Select a rule from the Rule List on the left."), MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        ruleDetailScrollPosition = EditorGUILayout.BeginScrollView(ruleDetailScrollPosition);
        SerializedProperty selectedRuleProp = postProcessRulesProp.GetArrayElementAtIndex(selectedRuleIndex);
        DrawRuleEditor(selectedRuleProp);
        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical();
    }

    private void EnsureSelectedRuleIndex()
    {
        if (postProcessRulesProp.arraySize == 0)
        {
            selectedRuleIndex = -1;
            return;
        }

        if (selectedRuleIndex < 0 || selectedRuleIndex >= postProcessRulesProp.arraySize)
        {
            selectedRuleIndex = 0;
        }
    }

    private void DrawRuleEditor(SerializedProperty ruleProp)
    {
        SerializedProperty targetNameProp = ruleProp.FindPropertyRelative("targetName");
        SerializedProperty useOtherAvatarDefinitionProp = ruleProp.FindPropertyRelative("useOtherAvatarDefinition");
        SerializedProperty avatarDefinitionProp = ruleProp.FindPropertyRelative("avatarDefinition");
        SerializedProperty framesToDeleteProp = ruleProp.FindPropertyRelative("framesToDelete");
        SerializedProperty shiftToZeroFrameProp = ruleProp.FindPropertyRelative("shiftToZeroFrame");
        SerializedProperty genericExtractProp = ruleProp.FindPropertyRelative("genericExtract");
        SerializedProperty genericOutputModeProp = ruleProp.FindPropertyRelative("genericOutputMode");
        SerializedProperty genericExtractTargetsProp = ruleProp.FindPropertyRelative("genericExtractTargets");
        SerializedProperty ignoreScaleKeyProp = ruleProp.FindPropertyRelative("ignoreScaleKey");
        SerializedProperty fixScaleProp = ruleProp.FindPropertyRelative("fixScale");
        SerializedProperty fixScaleObjectsProp = ruleProp.FindPropertyRelative("fixScaleObjects");
        SerializedProperty eventMarkersProp = ruleProp.FindPropertyRelative("eventMarkers");
        SerializedProperty animationEventsProp = ruleProp.FindPropertyRelative("animationEvents");

        EditorGUILayout.PropertyField(targetNameProp, new GUIContent("Target Name", L10n.T("FBX名と完全一致（大文字小文字は無視）", "Case-insensitive exact match with the FBX file name")));

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Avatar Import", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(useOtherAvatarDefinitionProp, new GUIContent("Use Other Avatar Definition"));
        if (useOtherAvatarDefinitionProp.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(avatarDefinitionProp, new GUIContent("Avatar Definition"));
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Curve Post Process", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(framesToDeleteProp, new GUIContent("Frames To Delete"), true);
        EditorGUILayout.PropertyField(shiftToZeroFrameProp, new GUIContent("Shift To Zero Frame"));

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Generic Extract", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(genericExtractProp, new GUIContent("Enable Generic Extract"));

        if (genericExtractProp.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(genericOutputModeProp, new GUIContent("Output Mode", L10n.T("Merge=Humanoid clipにマージ / Separate=<fbxName>_generic.anim へ分離出力", "Merge = combine into the humanoid clip / Separate = emit a sibling <fbxName>_generic.anim")));
            DrawGenericExtractTargets(genericExtractTargetsProp);
            EditorGUILayout.PropertyField(ignoreScaleKeyProp, new GUIContent("Ignore Scale Key", L10n.T("抽出時にTransformのScaleキー(m_LocalScale)を除外する", "Exclude Transform Scale (m_LocalScale) keys during extraction")));
            EditorGUILayout.PropertyField(fixScaleProp, new GUIContent("Fix Scale"));
            DrawStringListEditor(fixScaleObjectsProp, "Fix Scale Objects");
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Event Markers (FBX value pulse → AnimationEvent)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            L10n.T(
                "対象オブジェクトの local position を「イベント無し=0付近 / イベント有り>0.5」で打つと、0.5 を超える各フレームに AnimationEvent が立ちます(しきい値を上回っている間は連続して発火)。値の変化で表現するため resample/圧縮に強い。GenericExtract と同じ targetObjectName 規約で検索。",
                "Raise the target object's local position above 0.5 (0 = no event) on the frames that should fire. Every frame above the threshold gets an Animation Event (events keep firing while above 0.5). Encoding timing as a value change keeps it robust to import resampling/compression. Matching follows the same rule as GenericExtract targets."),
            MessageType.None);
        DrawEventMarkers(eventMarkersProp);

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Animation Events (Manual)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            L10n.T(
                "[DEPRECATED] 手書きのAnimationEventsは非推奨です。Event Markers (FBX側マーカー)への移行を推奨します。",
                "[DEPRECATED] Manually authored Animation Events are deprecated. Prefer Event Markers driven by FBX keyframes."),
            MessageType.Warning);
        DrawAnimationEvents(animationEventsProp);
    }

    private void DrawEventMarkers(SerializedProperty eventMarkersProp)
    {
        EditorGUILayout.LabelField($"Markers: {eventMarkersProp.arraySize}", EditorStyles.miniBoldLabel);

        int deleteIndex = -1;

        for (int i = 0; i < eventMarkersProp.arraySize; i++)
        {
            SerializedProperty markerProp = eventMarkersProp.GetArrayElementAtIndex(i);
            SerializedProperty targetObjectNameProp = markerProp.FindPropertyRelative("targetObjectName");
            SerializedProperty functionNameProp = markerProp.FindPropertyRelative("functionName");
            SerializedProperty floatParameterProp = markerProp.FindPropertyRelative("floatParameter");
            SerializedProperty intParameterProp = markerProp.FindPropertyRelative("intParameter");
            SerializedProperty stringParameterProp = markerProp.FindPropertyRelative("stringParameter");
            SerializedProperty objectReferenceParameterProp = markerProp.FindPropertyRelative("objectReferenceParameter");

            string title = string.IsNullOrWhiteSpace(targetObjectNameProp.stringValue)
                ? $"Marker {i + 1}"
                : $"{i + 1}. {targetObjectNameProp.stringValue.Trim()} → {functionNameProp.stringValue}";

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Delete", GUILayout.Width(70)))
            {
                deleteIndex = i;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(targetObjectNameProp, new GUIContent("Target Object Name / Path", L10n.T("FBX内マーカーオブジェクトの名前またはhierarchy path", "Name or hierarchy path of the FBX marker object")));
            EditorGUILayout.PropertyField(functionNameProp, new GUIContent("Function Name", L10n.T("呼び出す関数名", "Function name to invoke")));
            EditorGUILayout.PropertyField(floatParameterProp, new GUIContent("Float Parameter"));
            EditorGUILayout.PropertyField(intParameterProp, new GUIContent("Int Parameter"));
            EditorGUILayout.PropertyField(stringParameterProp, new GUIContent("String Parameter"));
            EditorGUILayout.PropertyField(objectReferenceParameterProp, new GUIContent("Object Reference"));
            EditorGUILayout.EndVertical();

            if (deleteIndex >= 0)
            {
                break;
            }
        }

        if (deleteIndex >= 0)
        {
            eventMarkersProp.DeleteArrayElementAtIndex(deleteIndex);
        }

        if (GUILayout.Button("Add Event Marker"))
        {
            int newIndex = eventMarkersProp.arraySize;
            eventMarkersProp.InsertArrayElementAtIndex(newIndex);
            SerializedProperty newMarkerProp = eventMarkersProp.GetArrayElementAtIndex(newIndex);
            newMarkerProp.FindPropertyRelative("targetObjectName").stringValue = string.Empty;
            newMarkerProp.FindPropertyRelative("functionName").stringValue = string.Empty;
            newMarkerProp.FindPropertyRelative("floatParameter").floatValue = 0f;
            newMarkerProp.FindPropertyRelative("intParameter").intValue = 0;
            newMarkerProp.FindPropertyRelative("stringParameter").stringValue = string.Empty;
            newMarkerProp.FindPropertyRelative("objectReferenceParameter").objectReferenceValue = null;
        }
    }

    private void DrawAnimationEvents(SerializedProperty animationEventsProp)
    {
        EditorGUILayout.LabelField($"Events: {animationEventsProp.arraySize}", EditorStyles.miniBoldLabel);

        int deleteIndex = -1;

        for (int i = 0; i < animationEventsProp.arraySize; i++)
        {
            SerializedProperty eventProp = animationEventsProp.GetArrayElementAtIndex(i);
            SerializedProperty functionNameProp = eventProp.FindPropertyRelative("functionName");
            SerializedProperty normalizedTimeProp = eventProp.FindPropertyRelative("normalizedTime");
            SerializedProperty floatParameterProp = eventProp.FindPropertyRelative("floatParameter");
            SerializedProperty intParameterProp = eventProp.FindPropertyRelative("intParameter");
            SerializedProperty stringParameterProp = eventProp.FindPropertyRelative("stringParameter");
            SerializedProperty objectReferenceParameterProp = eventProp.FindPropertyRelative("objectReferenceParameter");

            string title = string.IsNullOrWhiteSpace(functionNameProp.stringValue)
                ? $"Event {i + 1}"
                : $"{i + 1}. {functionNameProp.stringValue.Trim()}";

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Delete", GUILayout.Width(70)))
            {
                deleteIndex = i;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(functionNameProp, new GUIContent("Function Name", L10n.T("呼び出す関数名", "Function name to invoke")));
            normalizedTimeProp.floatValue = EditorGUILayout.Slider(
                new GUIContent("Normalized Time", L10n.T("クリップ長に対する正規化位置 (0=先頭, 1=末尾)。デフォルト0.9=90%", "Position normalized to clip length (0=start, 1=end). Default 0.9 = 90%")),
                normalizedTimeProp.floatValue, 0f, 1f);
            EditorGUILayout.PropertyField(floatParameterProp, new GUIContent("Float Parameter"));
            EditorGUILayout.PropertyField(intParameterProp, new GUIContent("Int Parameter"));
            EditorGUILayout.PropertyField(stringParameterProp, new GUIContent("String Parameter"));
            EditorGUILayout.PropertyField(objectReferenceParameterProp, new GUIContent("Object Reference"));
            EditorGUILayout.EndVertical();

            if (deleteIndex >= 0)
            {
                break;
            }
        }

        if (deleteIndex >= 0)
        {
            animationEventsProp.DeleteArrayElementAtIndex(deleteIndex);
        }

        if (GUILayout.Button("Add Animation Event"))
        {
            int newIndex = animationEventsProp.arraySize;
            animationEventsProp.InsertArrayElementAtIndex(newIndex);
            SerializedProperty newEventProp = animationEventsProp.GetArrayElementAtIndex(newIndex);
            newEventProp.FindPropertyRelative("functionName").stringValue = string.Empty;
            newEventProp.FindPropertyRelative("normalizedTime").floatValue = 0.9f;
            newEventProp.FindPropertyRelative("floatParameter").floatValue = 0f;
            newEventProp.FindPropertyRelative("intParameter").intValue = 0;
            newEventProp.FindPropertyRelative("stringParameter").stringValue = string.Empty;
            newEventProp.FindPropertyRelative("objectReferenceParameter").objectReferenceValue = null;
        }
    }

    private void DrawGenericExtractTargets(SerializedProperty genericExtractTargetsProp)
    {
        EditorGUILayout.LabelField($"Extract Targets: {genericExtractTargetsProp.arraySize}", EditorStyles.miniBoldLabel);

        int deleteTargetIndex = -1;

        for (int i = 0; i < genericExtractTargetsProp.arraySize; i++)
        {
            SerializedProperty targetProp = genericExtractTargetsProp.GetArrayElementAtIndex(i);
            SerializedProperty targetObjectNameProp = targetProp.FindPropertyRelative("targetObjectName");
            SerializedProperty enableRepathProp = targetProp.FindPropertyRelative("enableRepath");
            SerializedProperty repathToProp = targetProp.FindPropertyRelative("repathTo");

            string title = string.IsNullOrWhiteSpace(targetObjectNameProp.stringValue)
                ? $"Target {i + 1}"
                : targetObjectNameProp.stringValue.Trim();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Delete", GUILayout.Width(70)))
            {
                deleteTargetIndex = i;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(targetObjectNameProp, new GUIContent("Target Object Name / Path"));
            EditorGUILayout.PropertyField(enableRepathProp, new GUIContent("Enable Repath"));
            if (enableRepathProp.boolValue)
            {
                EditorGUILayout.PropertyField(repathToProp, new GUIContent("Repath To"));
            }
            EditorGUILayout.EndVertical();

            if (deleteTargetIndex >= 0)
            {
                break;
            }
        }

        if (deleteTargetIndex >= 0)
        {
            genericExtractTargetsProp.DeleteArrayElementAtIndex(deleteTargetIndex);
        }

        if (GUILayout.Button("Add Extract Target"))
        {
            int newIndex = genericExtractTargetsProp.arraySize;
            genericExtractTargetsProp.InsertArrayElementAtIndex(newIndex);
            SerializedProperty newTargetProp = genericExtractTargetsProp.GetArrayElementAtIndex(newIndex);
            newTargetProp.FindPropertyRelative("targetObjectName").stringValue = string.Empty;
            newTargetProp.FindPropertyRelative("enableRepath").boolValue = false;
            newTargetProp.FindPropertyRelative("repathTo").stringValue = string.Empty;
        }
    }

    private void DrawStringListEditor(SerializedProperty listProp, string label)
    {
        EditorGUILayout.LabelField($"{label}: {listProp.arraySize}", EditorStyles.miniBoldLabel);

        int deleteIndex = -1;
        for (int i = 0; i < listProp.arraySize; i++)
        {
            SerializedProperty elementProp = listProp.GetArrayElementAtIndex(i);

            EditorGUILayout.BeginHorizontal();
            elementProp.stringValue = EditorGUILayout.TextField($"Element {i + 1}", elementProp.stringValue);
            if (GUILayout.Button("Delete", GUILayout.Width(70)))
            {
                deleteIndex = i;
            }
            EditorGUILayout.EndHorizontal();

            if (deleteIndex >= 0)
            {
                break;
            }
        }

        if (deleteIndex >= 0)
        {
            listProp.DeleteArrayElementAtIndex(deleteIndex);
        }

        if (GUILayout.Button($"Add {label}"))
        {
            int newIndex = listProp.arraySize;
            listProp.InsertArrayElementAtIndex(newIndex);
            listProp.GetArrayElementAtIndex(newIndex).stringValue = string.Empty;
        }
    }

    private void InitializeRule(SerializedProperty ruleProp)
    {
        ruleProp.FindPropertyRelative("targetName").stringValue = string.Empty;
        ruleProp.FindPropertyRelative("useOtherAvatarDefinition").boolValue = false;
        ruleProp.FindPropertyRelative("avatarDefinition").objectReferenceValue = null;
        ruleProp.FindPropertyRelative("framesToDelete").ClearArray();
        ruleProp.FindPropertyRelative("shiftToZeroFrame").boolValue = true;
        ruleProp.FindPropertyRelative("genericExtract").boolValue = false;
        ruleProp.FindPropertyRelative("genericOutputMode").enumValueIndex = (int)GenericOutputMode.Merge;
        ruleProp.FindPropertyRelative("genericExtractTargets").ClearArray();
        ruleProp.FindPropertyRelative("ignoreScaleKey").boolValue = false;
        ruleProp.FindPropertyRelative("fixScale").boolValue = false;
        ruleProp.FindPropertyRelative("fixScaleObjects").ClearArray();
        ruleProp.FindPropertyRelative("eventMarkers").ClearArray();
        ruleProp.FindPropertyRelative("animationEvents").ClearArray();
    }
}
