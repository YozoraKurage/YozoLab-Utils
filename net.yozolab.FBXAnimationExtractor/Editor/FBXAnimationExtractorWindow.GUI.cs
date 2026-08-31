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

        // 入出力はルールフォルダごとの設定。共通のディレクトリは持たない。
        if (CountExecutableFolders() == 0)
        {
            EditorGUILayout.HelpBox(
                L10n.T("Source Directory と Output Directory はルールフォルダごとに設定します。"
                       + "「New Folder」でフォルダを作り、その見出しの下で指定してください。",
                       "Source Directory and Output Directory are set per rule folder. "
                       + "Create one with \"New Folder\" and set them under its header."),
                MessageType.Info);
        }

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
        // Source と Output が揃った有効なフォルダが 1 つでもあれば実行できる。
        bool isValid = CountExecutableFolders() > 0;
        bool canRefresh = CollectAllOutputFolders().Count > 0;

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
                    L10n.T("差分キャッシュを無視し、各ルールフォルダのSource Directory配下の全FBXを強制的に再エクスポートします",
                           "Ignore the diff cache and force a full re-export of every FBX under each rule folder's Source Directory")),
                GUILayout.Height(34), GUILayout.Width(140)))
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    L10n.T("全再エクスポート", "Re-export All"),
                    L10n.T(
                        "キャッシュを無視して各ルールフォルダの全FBXを再エクスポートします。\nファイル数が多いと時間がかかります。実行しますか?",
                        "Ignore the cache and re-export every FBX in every rule folder.\nThis can take a while for large folders. Continue?"),
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
                    L10n.T("各Output Directory以下の全 .anim のカーブを削除して空アニメに戻します(GUIDは維持)",
                           "Clear all curves of every .anim under each Output Directory (GUIDs preserved)")),
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

        if (GUILayout.Button(new GUIContent("New Folder",
                L10n.T("Rule Listに新しいフォルダを作成します",
                       "Create a new folder in the Rule List")),
            GUILayout.Width(90)))
        {
            FolderNamePromptWindow.Open(
                L10n.T("新規フォルダ", "New Folder"),
                name => CreateFolder(name, null));
        }

        List<int> folderTargets = GetPasteTargetIndices();
        using (new EditorGUI.DisabledScope(folderTargets.Count == 0))
        {
            if (GUILayout.Button(new GUIContent($"Folder ({folderTargets.Count}) ▾",
                    L10n.T("チェック済みRule(未チェックなら選択中Rule)をフォルダへ移動",
                           "Move checked rules (or the selected rule when none are checked) to a folder")),
                GUILayout.Width(95)))
            {
                ShowFolderAssignMenu(folderTargets);
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    /// <summary>Source と Output がどちらも有効なフォルダを指していて、Extract が ON のフォルダ数。</summary>
    private int CountExecutableFolders()
    {
        if (settings?.ruleFolders == null) return 0;

        int count = 0;
        foreach (RuleFolderState folder in settings.ruleFolders)
        {
            if (folder == null || string.IsNullOrWhiteSpace(folder.name) || !folder.extractEnabled) continue;
            if (!IsValidFolderAsset(folder.sourceDirectory) || !IsValidFolderAsset(folder.outputDirectory)) continue;
            count++;
        }
        return count;
    }

    /// <summary>
    /// フォルダの Source Directory 配下の FBX を走査し、未登録の名前を
    /// そのフォルダの Rule として追加する。
    /// </summary>
    private void AutoCollectFromFolder(RuleFolderState folder)
    {
        if (folder == null || !IsValidFolderAsset(folder.sourceDirectory))
        {
            Debug.LogWarning("[FBX Animation Extractor] Source Directory is not set to a valid folder.");
            return;
        }

        string sourcePath = AssetDatabase.GetAssetPath(folder.sourceDirectory);
        List<string> fbxNames = AssetDatabase.FindAssets("t:Model", new[] { sourcePath })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.ToLower().EndsWith(".fbx"))
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (fbxNames.Count == 0)
        {
            Debug.LogWarning($"[FBX Animation Extractor] No FBX files found under \"{sourcePath}\".");
            return;
        }

        serializedSettings.ApplyModifiedProperties();
        Undo.RecordObject(settings, "Auto Collect Rules from Source");

        string folderName = folder.name.Trim();

        // 同じ名前の Rule が「このフォルダに」既にあるかだけを見る。
        // 別フォルダに同名があっても、そちらは別の FBX を指しているので足す。
        var existingNames = new HashSet<string>(
            settings.postProcessRules
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.targetName)
                            && string.Equals(r.folder?.Trim() ?? string.Empty, folderName,
                                             StringComparison.OrdinalIgnoreCase))
                .Select(r => r.targetName.Trim()),
            StringComparer.OrdinalIgnoreCase);

        int added = 0;
        foreach (string name in fbxNames)
        {
            if (!existingNames.Add(name.Trim())) continue;

            settings.postProcessRules.Add(new AnimationPostProcessRule
            {
                targetName = name,
                folder = folderName,
            });
            added++;
        }

        EditorUtility.SetDirty(settings);
        serializedSettings.Update();
        settings.SaveSettings();

        Debug.Log($"[FBX Animation Extractor] Auto Collect \"{folderName}\": scanned {fbxNames.Count} FBX, added {added} new rule(s).");
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

        // Rule が 0 でも、フォルダがあれば見出しと入出力の設定を出す必要がある
        // (フォルダを作った直後がこの状態になる)。
        bool hasFolder = settings.ruleFolders != null && settings.ruleFolders.Count > 0;
        if (postProcessRulesProp.arraySize == 0 && !hasFolder)
        {
            EditorGUILayout.HelpBox(
                L10n.T("フォルダもルールもありません。「New Folder」でフォルダを作り、Source / Output Directory を設定してください。",
                       "No folders or rules yet. Create one with \"New Folder\" and set its Source / Output Directory."),
                MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        SyncRuleFolders();

        ruleListScrollPosition = EditorGUILayout.BeginScrollView(ruleListScrollPosition);

        string normalizedSearch = string.IsNullOrWhiteSpace(ruleSearchText)
            ? string.Empty
            : ruleSearchText.Trim().ToLowerInvariant();
        bool searching = !string.IsNullOrEmpty(normalizedSearch);

        // フォルダごとに index をグループ化(Rule の並び順は維持)
        var rootIndices = new List<int>();
        var folderBuckets = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < postProcessRulesProp.arraySize; i++)
        {
            string folderName = GetRuleFolderName(i);
            if (string.IsNullOrEmpty(folderName))
            {
                rootIndices.Add(i);
                continue;
            }

            if (!folderBuckets.TryGetValue(folderName, out List<int> bucket))
            {
                bucket = new List<int>();
                folderBuckets.Add(folderName, bucket);
            }
            bucket.Add(i);
        }

        // フォルダなしの Rule。入出力はフォルダが持つようになったので、
        // ここに置かれた Rule は Execute では走らない。黙って外れると気付けないため明示する。
        var visibleRootIndices = new List<int>();
        foreach (int i in rootIndices)
        {
            if (RuleMatchesSearch(i, normalizedSearch)) visibleRootIndices.Add(i);
        }

        if (visibleRootIndices.Count > 0)
        {
            EditorGUILayout.HelpBox(
                L10n.T("以下のRuleはフォルダに属していないため、Executeでは処理されません。"
                       + "「Folder ▾」でフォルダへ移動してください。",
                       "The rules below belong to no folder, so Execute skips them. "
                       + "Move them into a folder with \"Folder ▾\"."),
                MessageType.Warning);

            foreach (int i in visibleRootIndices)
            {
                DrawRuleListItem(i);
            }
        }

        // フォルダごとの Rule(空のフォルダも見出しだけ表示する)
        foreach (RuleFolderState folder in settings.ruleFolders)
        {
            if (folder == null || string.IsNullOrWhiteSpace(folder.name))
            {
                continue;
            }

            if (!folderBuckets.TryGetValue(folder.name.Trim(), out List<int> indices))
            {
                indices = new List<int>();
            }

            var visibleIndices = new List<int>();
            foreach (int i in indices)
            {
                if (RuleMatchesSearch(i, normalizedSearch)) visibleIndices.Add(i);
            }

            // 検索中は一致する Rule が無いフォルダを丸ごと隠し、あるフォルダは強制展開する
            if (searching && visibleIndices.Count == 0)
            {
                continue;
            }

            bool expanded = DrawFolderHeader(folder, indices.Count, searching);
            if (!expanded)
            {
                continue;
            }

            DrawFolderDirectories(folder);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(14);
            EditorGUILayout.BeginVertical();
            foreach (int i in visibleIndices)
            {
                DrawRuleListItem(i);
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
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
            if (RuleMatchesSearch(i, normalizedSearch))
            {
                checkedRuleIndices.Add(i);
            }
        }
    }

    private bool RuleMatchesSearch(int index, string normalizedSearch)
    {
        if (string.IsNullOrEmpty(normalizedSearch))
        {
            return true;
        }

        SerializedProperty ruleProp = postProcessRulesProp.GetArrayElementAtIndex(index);
        string name = ruleProp.FindPropertyRelative("targetName").stringValue ?? string.Empty;
        return name.Trim().ToLowerInvariant().IndexOf(normalizedSearch, StringComparison.Ordinal) >= 0;
    }

    /// <summary>Rule List の 1 行(チェックボックス + 選択ボタン + 生成 Clip ショートカット)を描画する。</summary>
    private void DrawRuleListItem(int i)
    {
        SerializedProperty ruleProp = postProcessRulesProp.GetArrayElementAtIndex(i);
        SerializedProperty targetNameProp = ruleProp.FindPropertyRelative("targetName");

        string targetName = string.IsNullOrWhiteSpace(targetNameProp.stringValue)
            ? "(No Target Name)"
            : targetNameProp.stringValue.Trim();

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

        // 生成済みクリップは Rule 自身から解決する。名前だけで引くと、出力先の違う
        // 別フォルダの Rule や、同名・出力名違いで並べた Rule が同じクリップを指してしまう。
        DrawGeneratedClipShortcut(i, targetNameProp.stringValue);
        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// フォルダ見出し行を描画する。折りたたみと「このフォルダをExtractするか」のトグルを持つ。
    /// 戻り値は中身を描画すべきかどうか(検索中は強制展開)。
    /// </summary>
    private bool DrawFolderHeader(RuleFolderState folder, int ruleCount, bool searching)
    {
        EditorGUILayout.BeginHorizontal("box");

        bool shownExpanded = searching || folder.expanded;
        bool nowExpanded = EditorGUILayout.Foldout(shownExpanded, $"{folder.name}  ({ruleCount})", true, EditorStyles.foldoutHeader);
        if (!searching && nowExpanded != folder.expanded)
        {
            folder.expanded = nowExpanded;
            EditorUtility.SetDirty(settings);
        }

        GUILayout.FlexibleSpace();

        // Extract フラグはフォルダ見出しに常時表示して、すぐ切り替えられるようにする
        Color prevBg = GUI.backgroundColor;
        if (!folder.extractEnabled)
        {
            GUI.backgroundColor = new Color(1f, 0.55f, 0.45f, 0.9f);
        }
        EditorGUI.BeginChangeCheck();
        bool extract = GUILayout.Toggle(folder.extractEnabled,
            new GUIContent(folder.extractEnabled ? "Extract: ON" : "Extract: OFF",
                L10n.T("OFFにすると、このフォルダ内のRuleに一致するFBXはExecuteで処理されません",
                       "When OFF, FBX files matching rules in this folder are skipped by Execute")),
            EditorStyles.miniButton, GUILayout.Width(90));
        GUI.backgroundColor = prevBg;
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(settings, "Toggle Folder Extract");
            folder.extractEnabled = extract;
            EditorUtility.SetDirty(settings);
            settings.SaveSettings();
        }

        using (new EditorGUI.DisabledScope(!IsValidFolderAsset(folder.sourceDirectory)))
        {
            if (GUILayout.Button(new GUIContent("Collect",
                    L10n.T("このフォルダのSource Directory配下のFBXを走査し、未登録の名前をこのフォルダのRuleとして追加します",
                           "Scan FBX under this folder's Source Directory and add unregistered names as rules in this folder")),
                EditorStyles.miniButton, GUILayout.Width(60)))
            {
                AutoCollectFromFolder(folder);
                GUIUtility.ExitGUI(); // Rule が増えてリスト構造が変わる
            }
        }

        if (GUILayout.Button(new GUIContent("✕",
                L10n.T("フォルダを削除(中のRuleはフォルダなしに戻ります)",
                       "Delete this folder (rules inside are moved out of folders)")),
            EditorStyles.miniButton, GUILayout.Width(20)))
        {
            DeleteFolder(folder, ruleCount);
            GUIUtility.ExitGUI(); // リスト構造が変わるので今フレームの描画を打ち切る
        }

        EditorGUILayout.EndHorizontal();
        return searching || folder.expanded;
    }

    /// <summary>
    /// フォルダの入出力ディレクトリ。見出しの直下、Rule の並びより前に置く。
    /// 見出し行へ押し込むと横幅が足りないので、展開したときだけ縦に並べる。
    /// </summary>
    private void DrawFolderDirectories(RuleFolderState folder)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(14);
        EditorGUILayout.BeginVertical();

        EditorGUI.BeginChangeCheck();

        var source = (DefaultAsset)EditorGUILayout.ObjectField(
            new GUIContent("Source Directory",
                L10n.T("このフォルダが処理するFBXの置き場",
                       "Folder containing the FBX files this rule folder processes")),
            folder.sourceDirectory, typeof(DefaultAsset), false);

        var output = (DefaultAsset)EditorGUILayout.ObjectField(
            new GUIContent("Output Directory",
                L10n.T("このフォルダの既定の出力先。Rule 側で個別に上書きできます",
                       "Default output folder for this rule folder. Individual rules can override it")),
            folder.outputDirectory, typeof(DefaultAsset), false);

        if (EditorGUI.EndChangeCheck())
        {
            serializedSettings.ApplyModifiedProperties();
            Undo.RecordObject(settings, "Set Folder Directories");
            folder.sourceDirectory = source;
            folder.outputDirectory = output;
            EditorUtility.SetDirty(settings);
            serializedSettings.Update();
            settings.SaveSettings();
        }

        // フォルダを指していない DefaultAsset(FBX や .anim を放り込んだ場合)は弾く
        if (folder.sourceDirectory != null && !IsValidFolderAsset(folder.sourceDirectory))
        {
            EditorGUILayout.HelpBox(
                L10n.T("Source Directory にはフォルダを指定してください。",
                       "Source Directory must be a folder."), MessageType.Warning);
        }
        else if (folder.sourceDirectory == null)
        {
            EditorGUILayout.HelpBox(
                L10n.T("Source Directory が未設定です。このフォルダは Execute の対象になりません。",
                       "Source Directory is not set; this folder is skipped by Execute."), MessageType.Warning);
        }

        if (folder.outputDirectory != null && !IsValidFolderAsset(folder.outputDirectory))
        {
            EditorGUILayout.HelpBox(
                L10n.T("Output Directory にはフォルダを指定してください。",
                       "Output Directory must be a folder."), MessageType.Warning);
        }
        else if (folder.outputDirectory == null)
        {
            EditorGUILayout.HelpBox(
                L10n.T("Output Directory が未設定です。このフォルダは Execute の対象になりません。",
                       "Output Directory is not set; this folder is skipped by Execute."), MessageType.Warning);
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>フォルダを削除する。中に Rule がある場合は確認のうえ、フォルダなしへ戻す。</summary>
    private void DeleteFolder(RuleFolderState folder, int ruleCount)
    {
        if (ruleCount > 0)
        {
            bool confirmed = EditorUtility.DisplayDialog(
                L10n.T("フォルダ削除", "Delete Folder"),
                L10n.T(
                    $"フォルダ \"{folder.name}\" を削除します。\n中の {ruleCount} 個のRuleは削除されず、フォルダなしに戻ります。よろしいですか?",
                    $"Delete folder \"{folder.name}\"?\nThe {ruleCount} rule(s) inside are kept and moved out of the folder."),
                L10n.T("削除", "Delete"),
                L10n.T("キャンセル", "Cancel"));
            if (!confirmed) return;
        }

        serializedSettings.ApplyModifiedProperties();
        Undo.RecordObject(settings, "Delete Rule Folder");

        string key = folder.name.Trim();
        if (settings.postProcessRules != null)
        {
            foreach (AnimationPostProcessRule rule in settings.postProcessRules)
            {
                if (rule != null && !string.IsNullOrWhiteSpace(rule.folder)
                    && string.Equals(rule.folder.Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    rule.folder = string.Empty;
                }
            }
        }
        settings.ruleFolders.Remove(folder);

        EditorUtility.SetDirty(settings);
        serializedSettings.Update();
        settings.SaveSettings();
        Repaint();
    }

    /// <summary>
    /// settings.ruleFolders を整える。フォルダは New Folder ボタンで明示的に作成/削除するため
    /// ここでは削除しない。名前が空・重複のエントリの除去と、Rule 側だけに存在する
    /// フォルダ名(旧データ等)の補完のみ行う。
    /// </summary>
    private void SyncRuleFolders()
    {
        if (settings.ruleFolders == null)
        {
            settings.ruleFolders = new List<RuleFolderState>();
        }

        bool changed = false;

        // 名前が空のエントリと重複エントリを除去
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = settings.ruleFolders.Count - 1; i >= 0; i--)
        {
            RuleFolderState folder = settings.ruleFolders[i];
            if (folder == null || string.IsNullOrWhiteSpace(folder.name))
            {
                settings.ruleFolders.RemoveAt(i);
                changed = true;
            }
        }
        for (int i = 0; i < settings.ruleFolders.Count; i++)
        {
            if (!seen.Add(settings.ruleFolders[i].name.Trim()))
            {
                settings.ruleFolders.RemoveAt(i);
                i--;
                changed = true;
            }
        }

        // Rule 側だけに存在するフォルダ名を補完(旧バージョンのデータ読み込み対策)
        if (settings.postProcessRules != null)
        {
            foreach (AnimationPostProcessRule rule in settings.postProcessRules)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.folder)) continue;
                string name = rule.folder.Trim();
                if (seen.Add(name))
                {
                    settings.ruleFolders.Add(new RuleFolderState { name = name });
                    changed = true;
                }
            }
        }

        if (changed)
        {
            EditorUtility.SetDirty(settings);
        }
    }

    private string GetRuleFolderName(int index)
    {
        SerializedProperty folderProp = postProcessRulesProp.GetArrayElementAtIndex(index).FindPropertyRelative("folder");
        return folderProp == null || string.IsNullOrWhiteSpace(folderProp.stringValue)
            ? string.Empty
            : folderProp.stringValue.Trim();
    }

    /// <summary>作成済みフォルダの名前一覧(重複なし・定義順)を返す。</summary>
    private List<string> CollectFolderNames()
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (settings == null || settings.ruleFolders == null) return names;

        foreach (RuleFolderState folder in settings.ruleFolders)
        {
            if (folder == null || string.IsNullOrWhiteSpace(folder.name)) continue;
            string name = folder.name.Trim();
            if (seen.Add(name)) names.Add(name);
        }
        return names;
    }

    /// <summary>登録済みフォルダを名前(大文字小文字無視)で探す。</summary>
    private RuleFolderState FindFolderState(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName) || settings == null || settings.ruleFolders == null)
        {
            return null;
        }

        string key = folderName.Trim();
        foreach (RuleFolderState folder in settings.ruleFolders)
        {
            if (folder != null && string.Equals(folder.name?.Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                return folder;
            }
        }
        return null;
    }

    private void ShowFolderAssignMenu(List<int> ruleIndices)
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent(L10n.T("(フォルダなし)", "(No Folder)")), false,
            () => AssignFolderToRules(ruleIndices, string.Empty));

        List<string> names = CollectFolderNames();
        if (names.Count > 0)
        {
            menu.AddSeparator(string.Empty);
            foreach (string name in names)
            {
                string captured = name;
                menu.AddItem(new GUIContent(captured), false, () => AssignFolderToRules(ruleIndices, captured));
            }
        }

        menu.AddSeparator(string.Empty);
        menu.AddItem(new GUIContent(L10n.T("新規フォルダ...", "New Folder...")), false, () =>
            FolderNamePromptWindow.Open(
                L10n.T("新規フォルダ", "New Folder"),
                name => CreateFolder(name, ruleIndices)));

        menu.ShowAsContext();
    }

    /// <summary>
    /// フォルダを作成する。同名(大文字小文字無視)が既にあればそれを使う。
    /// assignRuleIndices が指定されていれば、その Rule をフォルダへ移動する。
    /// </summary>
    private void CreateFolder(string folderName, List<int> assignRuleIndices)
    {
        folderName = folderName?.Trim();
        if (string.IsNullOrEmpty(folderName)) return;

        RuleFolderState existing = FindFolderState(folderName);
        if (existing == null)
        {
            Undo.RecordObject(settings, "Create Rule Folder");
            settings.ruleFolders.Add(new RuleFolderState { name = folderName });
            EditorUtility.SetDirty(settings);
            settings.SaveSettings();
            Debug.Log($"[FBX Animation Extractor] Created folder \"{folderName}\".");
        }
        else
        {
            // 既存フォルダ名で作成した場合はそこへ合流させる(見た目の名前は既存側を維持)
            folderName = existing.name.Trim();
        }

        if (assignRuleIndices != null && assignRuleIndices.Count > 0)
        {
            AssignFolderToRules(assignRuleIndices, folderName);
        }
        else
        {
            Repaint();
        }
    }

    private void AssignFolderToRules(List<int> ruleIndices, string folderName)
    {
        if (ruleIndices == null || ruleIndices.Count == 0) return;

        serializedSettings.ApplyModifiedProperties();
        Undo.RecordObject(settings, "Set Rule Folder");

        int applied = 0;
        foreach (int i in ruleIndices)
        {
            if (i < 0 || i >= settings.postProcessRules.Count) continue;
            AnimationPostProcessRule rule = settings.postProcessRules[i];
            if (rule == null) continue;
            rule.folder = folderName;
            applied++;
        }

        EditorUtility.SetDirty(settings);
        serializedSettings.Update();
        settings.SaveSettings();
        Repaint();

        string label = string.IsNullOrEmpty(folderName) ? "(No Folder)" : folderName;
        Debug.Log($"[FBX Animation Extractor] Moved {applied} rule(s) to folder \"{label}\".");
    }

    private void DrawGeneratedClipShortcut(int ruleIndex, string targetName)
    {
        AnimationPostProcessRule rule = GetRuleAt(ruleIndex);
        AnimationClip generatedClip = ResolveGeneratedClipForRule(rule, targetName);
        AnimationClip generatedGenericClip = ResolveGeneratedGenericClipForRule(rule, targetName);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("Clip", generatedClip, typeof(AnimationClip), false);
            if (generatedGenericClip != null)
            {
                EditorGUILayout.ObjectField("Generic", generatedGenericClip, typeof(AnimationClip), false);
            }
        }
    }

    private AnimationPostProcessRule GetRuleAt(int index)
    {
        if (settings?.postProcessRules == null) return null;
        return index >= 0 && index < settings.postProcessRules.Count ? settings.postProcessRules[index] : null;
    }

    /// <summary>
    /// Rule が書き出す .anim のパス。Rule の出力先 / 出力名から素直に組み立てる。
    /// 以前は差分キャッシュを FBX 名で引いていたが、同名 Rule が並ぶと全員が
    /// 同じ 1 本を指してしまうので、Rule 自身から決める。
    /// </summary>
    private string BuildExpectedClipPath(AnimationPostProcessRule rule, string targetName, bool generic)
    {
        if (rule == null || string.IsNullOrWhiteSpace(targetName))
        {
            return string.Empty;
        }

        string outputPath = GetRuleOutputFolder(rule);
        if (string.IsNullOrEmpty(outputPath))
        {
            return string.Empty;
        }

        string outputName = GetRuleOutputName(rule, targetName.Trim());
        return generic ? $"{outputPath}/{outputName}_generic.anim" : $"{outputPath}/{outputName}.anim";
    }

    private AnimationClip ResolveGeneratedGenericClipForRule(AnimationPostProcessRule rule, string targetName)
    {
        if (rule == null || !rule.genericExtract || rule.genericOutputMode != GenericOutputMode.Separate)
        {
            return null;
        }

        string clipPath = BuildExpectedClipPath(rule, targetName, generic: true);
        return string.IsNullOrEmpty(clipPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
    }

    private AnimationClip ResolveGeneratedClipForRule(AnimationPostProcessRule rule, string targetName)
    {
        string clipPath = BuildExpectedClipPath(rule, targetName, generic: false);
        return string.IsNullOrEmpty(clipPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
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
        SerializedProperty folderProp = ruleProp.FindPropertyRelative("folder");
        SerializedProperty outputFileNameProp = ruleProp.FindPropertyRelative("outputFileName");
        SerializedProperty outputDirectoryOverrideProp = ruleProp.FindPropertyRelative("outputDirectoryOverride");
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

        EditorGUILayout.PropertyField(targetNameProp, new GUIContent("Target Name", L10n.T("FBX名と完全一致（大文字小文字は無視）", "Case-insensitive exact match with the FBX file name")));
        DrawRuleFolderField(folderProp);

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(outputFileNameProp, new GUIContent("Output File Name", L10n.T("書き出す .anim のファイル名(拡張子不要)。未設定ならFBX名を使用します", "File name of the exported .anim (no extension). Leave empty to use the FBX name")));
        if (!string.IsNullOrWhiteSpace(outputFileNameProp.stringValue))
        {
            EditorGUILayout.LabelField(" ",
                $"→ {SanitizeOutputName(outputFileNameProp.stringValue, targetNameProp.stringValue?.Trim())}.anim",
                EditorStyles.miniLabel);
        }
        EditorGUILayout.PropertyField(outputDirectoryOverrideProp, new GUIContent("Output Directory (Override)", L10n.T("このRule専用の出力先フォルダ。未設定なら所属フォルダのOutput Directoryを使用します", "Per-rule output folder. Leave empty to use the rule folder's Output Directory")));
        if (outputDirectoryOverrideProp.objectReferenceValue != null)
        {
            string overridePath = AssetDatabase.GetAssetPath(outputDirectoryOverrideProp.objectReferenceValue);
            if (string.IsNullOrEmpty(overridePath) || !AssetDatabase.IsValidFolder(overridePath))
            {
                EditorGUILayout.HelpBox(
                    L10n.T("Output Directory (Override) にはフォルダを指定してください。所属フォルダのOutput Directoryにフォールバックします。",
                           "Output Directory (Override) must be a folder. Falling back to the rule folder's Output Directory."),
                    MessageType.Warning);
            }
        }

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

    /// <summary>
    /// Folder の選択ドロップダウンを描く。フォルダの作成はメニューの「新規フォルダ...」
    /// または Rule List 上部の New Folder ボタンから明示的に行う(テキスト入力起点にしない)。
    /// </summary>
    private void DrawRuleFolderField(SerializedProperty folderProp)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(new GUIContent("Folder",
            L10n.T("Rule Listでのグループ。ドロップダウンから選択します",
                   "Group in the Rule List. Pick one from the dropdown")));

        string current = string.IsNullOrWhiteSpace(folderProp.stringValue)
            ? L10n.T("(フォルダなし)", "(No Folder)")
            : folderProp.stringValue.Trim();
        if (GUILayout.Button(current, EditorStyles.popup))
        {
            ShowFolderAssignMenu(new List<int> { selectedRuleIndex });
        }
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>フォルダ名を入力させる小さなモーダルウィンドウ。Enter で確定、Esc でキャンセル。</summary>
    private sealed class FolderNamePromptWindow : EditorWindow
    {
        private string folderName = string.Empty;
        private Action<string> onConfirm;
        private bool focusRequested = true;

        public static void Open(string title, Action<string> onConfirm)
        {
            var window = CreateInstance<FolderNamePromptWindow>();
            window.titleContent = new GUIContent(title);
            window.onConfirm = onConfirm;
            window.minSize = new Vector2(340f, 80f);
            window.maxSize = new Vector2(340f, 80f);
            window.ShowModalUtility();
        }

        private void OnGUI()
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    e.Use();
                    Confirm();
                    return;
                }
                if (e.keyCode == KeyCode.Escape)
                {
                    e.Use();
                    Close();
                    return;
                }
            }

            EditorGUILayout.Space(8);
            GUI.SetNextControlName("FolderNameField");
            folderName = EditorGUILayout.TextField(L10n.T("フォルダ名", "Folder Name"), folderName);
            if (focusRequested)
            {
                EditorGUI.FocusTextInControl("FolderNameField");
                focusRequested = false;
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(folderName)))
            {
                if (GUILayout.Button(L10n.T("作成", "Create"), GUILayout.Width(90)))
                {
                    Confirm();
                }
            }
            if (GUILayout.Button(L10n.T("キャンセル", "Cancel"), GUILayout.Width(90)))
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void Confirm()
        {
            if (string.IsNullOrWhiteSpace(folderName)) return;
            Action<string> callback = onConfirm;
            string name = folderName.Trim();
            Close();
            callback?.Invoke(name);
        }
    }

    private void InitializeRule(SerializedProperty ruleProp)
    {
        ruleProp.FindPropertyRelative("targetName").stringValue = string.Empty;
        ruleProp.FindPropertyRelative("folder").stringValue = string.Empty;
        ruleProp.FindPropertyRelative("outputFileName").stringValue = string.Empty;
        ruleProp.FindPropertyRelative("outputDirectoryOverride").objectReferenceValue = null;
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
    }
}
