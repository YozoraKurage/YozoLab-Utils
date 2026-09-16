using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using System;
using System.Linq;
using System.Collections.Generic;

namespace YozoLab.FBXAnimationBaker
{
    /// <summary>
    /// FBXAnimationBakerWindow の GUI 描画担当。OnGUI から各ペインの描画までをここにまとめる。
    /// </summary>
    public partial class FBXAnimationBakerWindow
    {
        // リストペインの幅の下限・上限。上限はウィンドウ幅から詳細ペインの取り分を引いて決める。
        private const float EntryListMinWidth = 170f;
        private const float EntryDetailMinWidth = 300f;
        private const float SplitterWidth = 5f;

        private void OnGUI()
        {
            // 再生/停止を挟むと ScriptableSingleton が破棄・再生成され、既存の SerializedObject が
            // ターゲット破棄済みで無効になる。破棄を検知して再ロードする。
            if (serializedSettings == null || serializedSettings.targetObject == null || settings == null)
            {
                LoadOrCreateSettings();
            }

            serializedSettings.Update();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("FBX Animation Baker", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Exporter Info",
                    L10n.T("見つかったFBX Exporterのバージョンやオプションの状態をConsoleに出力します(形式が変わらないときの切り分け用)",
                           "Log the detected FBX Exporter version and option state to the Console (for troubleshooting export format issues)")),
                GUILayout.Width(95)))
            {
                FbxExporterBridge.ClearCache();
                FbxExporterBridge.LogDiagnostics();
            }
            if (GUILayout.Button(L10n.IsEnglish ? "EN" : "JP", GUILayout.Width(35)))
            {
                L10n.IsEnglish = !L10n.IsEnglish;
                Repaint();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            EditorGUILayout.LabelField(L10n.T(
                "FBX とモーション(AnimationClip / BVH)を指定すると、それを Transform アニメーションとしてベイクした FBX を書き出します。",
                "Pick an FBX and motions (animation clips or BVH) to export an FBX with them baked as Transform animation."),
                EditorStyles.wordWrappedMiniLabel);

            if (!FbxExporterBridge.IsAvailable)
            {
                EditorGUILayout.HelpBox(L10n.T(
                    "Unity FBX Exporter (com.unity.formats.fbx) が見つかりません。Package Manager からインストールしてください。",
                    "Unity FBX Exporter (com.unity.formats.fbx) was not found. Install it from Package Manager."),
                    MessageType.Error);

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                // 探しに行くのはユーザーの手間なので、その場で入れられるようにする。
                // 解決するとドメインリロードが走り、次の描画では IsAvailable が true になる。
                using (new EditorGUI.DisabledScope(fbxExporterInstall != null && !fbxExporterInstall.IsCompleted))
                {
                    if (GUILayout.Button(L10n.T("インストール", "Install"), GUILayout.Width(110)))
                    {
                        fbxExporterInstall = Client.Add(FbxExporterPackageId);
                    }
                }

                if (GUILayout.Button(L10n.T("Package Manager", "Package Manager"), GUILayout.Width(130)))
                {
                    UnityEditor.PackageManager.UI.Window.Open(FbxExporterPackageId);
                }

                if (GUILayout.Button(L10n.T("再チェック", "Re-check"), GUILayout.Width(110)))
                {
                    FbxExporterBridge.ClearCache();
                }
                EditorGUILayout.EndHorizontal();

                if (fbxExporterInstall != null && !fbxExporterInstall.IsCompleted)
                {
                    EditorGUILayout.LabelField(L10n.T("インストール中…", "Installing…"), EditorStyles.miniLabel);
                    Repaint();
                }
                else if (fbxExporterInstall != null && fbxExporterInstall.Status == StatusCode.Failure)
                {
                    EditorGUILayout.HelpBox(
                        L10n.T($"インストールに失敗しました: {fbxExporterInstall.Error?.message}",
                               $"Install failed: {fbxExporterInstall.Error?.message}"),
                        MessageType.Error);
                }
            }

            // 出力先はフォルダごとの設定。共通の Output Directory は持たない。
            if (CountExecutableFolders() == 0)
            {
                EditorGUILayout.HelpBox(
                    L10n.T("Output Directory はフォルダごとに設定します。"
                           + "「New Folder」でフォルダを作り、その見出しの下で指定してください。",
                           "Output Directory is set per folder. Create one with \"New Folder\" "
                           + "and set it under the folder header."),
                    MessageType.Info);
            }

            EditorGUILayout.Space();

            DrawEntriesSection();
            DrawExecuteBar();

            serializedSettings.ApplyModifiedProperties();
        }

        private void DrawEntriesSection()
        {
            EditorGUILayout.BeginVertical("box", GUILayout.ExpandHeight(true));
            EnsureSelectedEntryIndex();

            // 操作はそれが効く場所の近くへ置く。リストを触るものはリストの中、
            // 設定を触るものは設定ペインの中。全幅のツールバーに並べると、
            // リストを操作するボタンが右端(＝詳細ペインの上)に出てしまう。
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            DrawEntryListPane();
            DrawPaneSplitter();
            DrawEntryDetailPane();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// リストペインの幅。ユーザーごとの見た目の好みなので EditorPrefs に持つ
        /// （プロジェクト設定に混ぜると、他の人の環境まで動いてしまう）。
        /// ウィンドウを狭めたときに詳細ペインが潰れないよう、読むたびに現在の幅で丸める。
        /// </summary>
        private float EntryListWidth
        {
            get
            {
                if (entryListWidth < 0f)
                {
                    entryListWidth = EditorPrefs.GetFloat(EntryListWidthPrefKey, 270f);
                }

                float max = Mathf.Max(EntryListMinWidth, position.width - EntryDetailMinWidth - SplitterWidth);
                return Mathf.Clamp(entryListWidth, EntryListMinWidth, max);
            }
        }

        /// <summary>
        /// リストと設定のあいだの仕切り。掴んで幅を変えられる。
        /// 幅の決め打ちだと、エントリ名が長い人にも短い人にも合わない。
        /// </summary>
        private void DrawPaneSplitter()
        {
            Rect rect = GUILayoutUtility.GetRect(SplitterWidth, SplitterWidth, GUILayout.ExpandHeight(true));

            if (Event.current.type == EventType.Repaint)
            {
                // 掴めることが分かる程度の線。掴んでいる間は濃くする。
                var line = new Rect(rect.x + rect.width * 0.5f - 1f, rect.y + 2f, 2f, rect.height - 4f);
                EditorGUI.DrawRect(line, draggingSplitter
                    ? new Color(0.35f, 0.58f, 0.85f, 0.9f)
                    : new Color(0f, 0f, 0f, 0.25f));
            }

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when rect.Contains(e.mousePosition) && e.button == 0:
                    draggingSplitter = true;
                    e.Use();
                    break;

                // 動かした距離で足し引きする。マウス座標をそのまま幅にすると、
                // 外側の box の余白ぶんだけ食い違う。
                case EventType.MouseDrag when draggingSplitter:
                    entryListWidth = EntryListWidth + (e.mousePosition.x - rect.center.x);
                    e.Use();
                    Repaint();
                    break;

                // 保存はドラッグが終わってから。動かすたびに EditorPrefs へ書かない。
                case EventType.MouseUp when draggingSplitter:
                    draggingSplitter = false;
                    EditorPrefs.SetFloat(EntryListWidthPrefKey, EntryListWidth);
                    e.Use();
                    break;
            }
        }

        /// <summary>リストの上に置く操作。エントリを増やす・減らす・フォルダを作る。</summary>
        private void DrawEntryListToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Entries: {bakeEntriesProp.arraySize}", EditorStyles.miniBoldLabel,
                                       GUILayout.Width(70));
            GUILayout.Label("🔍", GUILayout.Width(16));
            entrySearchText = EditorGUILayout.TextField(entrySearchText);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(new GUIContent("+ Add", L10n.T("空のエントリを追加", "Add an empty entry")),
                    EditorStyles.miniButtonLeft))
            {
                AddEntry(null, null);
            }

            if (GUILayout.Button(new GUIContent(L10n.T("選択から", "From Sel."),
                    L10n.T("Projectで選択中のFBXとAnimationClipからエントリを作成します",
                           "Create entries from the FBX models and animation clips selected in the Project window")),
                EditorStyles.miniButtonMid))
            {
                AddEntriesFromSelection();
            }

            using (new EditorGUI.DisabledScope(!IsEntryIndexValid(selectedEntryIndex)))
            {
                if (GUILayout.Button(new GUIContent(L10n.T("複製", "Duplicate"),
                        L10n.T("選択中エントリを複製", "Duplicate the selected entry")), EditorStyles.miniButtonMid))
                {
                    bakeEntriesProp.InsertArrayElementAtIndex(selectedEntryIndex);
                    selectedEntryIndex++;
                    selectedEntryIndices.Clear();
                    selectedEntryIndices.Add(selectedEntryIndex);

                    SerializedProperty nameProp = bakeEntriesProp
                        .GetArrayElementAtIndex(selectedEntryIndex)
                        .FindPropertyRelative("displayName");
                    if (!string.IsNullOrWhiteSpace(nameProp.stringValue))
                    {
                        nameProp.stringValue = $"{nameProp.stringValue}_copy";
                    }
                }

                if (GUILayout.Button(new GUIContent(L10n.T("削除", "Delete"),
                        L10n.T("選択中エントリを削除", "Delete the selected entry")), EditorStyles.miniButtonMid))
                {
                    bakeEntriesProp.DeleteArrayElementAtIndex(selectedEntryIndex);
                    selectedEntryIndices.Clear();
                    selectedEntryIndex = Mathf.Clamp(selectedEntryIndex, 0, bakeEntriesProp.arraySize - 1);
                }
            }

            if (GUILayout.Button(new GUIContent(L10n.T("新規フォルダ", "New Folder"),
                    L10n.T("フォルダを作ります。名前は見出しでそのまま直せます",
                           "Create a folder. Rename it directly in its header")),
                EditorStyles.miniButtonRight))
            {
                CreateFolder(MakeUniqueFolderName(L10n.T("新しいフォルダ", "New Folder")), null);
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 設定ペインの上に置くテンプレート操作。
        /// 写す元も貼る先もこのペインに映っている設定なので、ここが置き場になる。
        /// </summary>
        private void DrawTemplateToolbar()
        {
            EditorGUILayout.BeginHorizontal();

            string source = entryTemplate == null
                ? L10n.T("(未設定)", "(empty)")
                : $"\"{entryTemplate.sourceEntryName}\"";
            EditorGUILayout.LabelField(new GUIContent(
                $"Template: {source}",
                L10n.T("選択中エントリのベイク設定をコピーし、複数のエントリに貼り付けできます(FBX/モーション/名前は除く)",
                       "Copy the selected entry's bake settings and paste them to several entries (excluding FBX, motions and name)")),
                EditorStyles.miniBoldLabel);

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(!IsEntryIndexValid(selectedEntryIndex)))
            {
                if (GUILayout.Button(new GUIContent(L10n.T("コピー", "Copy"),
                        L10n.T("選択中エントリからテンプレートをコピー(FBX/モーション/名前を除く)",
                               "Capture the selected entry as a template (excluding FBX, motions and name)")),
                    EditorStyles.miniButtonLeft, GUILayout.Width(70)))
                {
                    CopySelectedEntryToTemplate();
                }
            }

            List<int> pasteTargets = GetBatchTargetIndices();
            using (new EditorGUI.DisabledScope(entryTemplate == null || pasteTargets.Count == 0))
            {
                if (GUILayout.Button(new GUIContent(
                        $"{L10n.T("貼り付け", "Paste")} ({pasteTargets.Count})",
                        L10n.T("選択中のエントリすべてに貼り付け", "Paste to every selected entry")),
                    EditorStyles.miniButtonRight, GUILayout.Width(90)))
                {
                    PasteTemplateToEntries(pasteTargets);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawEntryListPane()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(EntryListWidth), GUILayout.ExpandHeight(true));

            DrawEntryListToolbar();

            // エントリが 0 でも、フォルダがあれば見出しと出力先の設定を出す必要がある
            // (フォルダを作った直後がこの状態)。
            bool hasFolder = settings.bakeFolders != null && settings.bakeFolders.Count > 0;
            if (bakeEntriesProp.arraySize == 0 && !hasFolder)
            {
                EditorGUILayout.HelpBox(
                    L10n.T("フォルダもエントリもありません。「New Folder」でフォルダを作り、Output Directory を設定してください。",
                           "No folders or entries yet. Create one with \"New Folder\" and set its Output Directory."),
                    MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            SyncBakeFolders();

            entryListScrollPosition = EditorGUILayout.BeginScrollView(entryListScrollPosition, "box");

            string normalizedSearch = string.IsNullOrWhiteSpace(entrySearchText)
                ? string.Empty
                : entrySearchText.Trim().ToLowerInvariant();
            bool searching = !string.IsNullOrEmpty(normalizedSearch);

            // フォルダごとに index をグループ化(エントリの並び順は維持)
            var rootIndices = new List<int>();
            var folderBuckets = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < bakeEntriesProp.arraySize; i++)
            {
                string folderName = GetEntryFolderName(i);
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

            // フォルダなしのエントリ。出力先はフォルダが持つようになったので、
            // ここに置かれたエントリは Execute では走らない。黙って外れると気付けないため明示する。
            var visibleRootIndices = new List<int>();
            foreach (int i in rootIndices)
            {
                if (EntryMatchesSearch(i, normalizedSearch)) visibleRootIndices.Add(i);
            }

            if (visibleRootIndices.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    L10n.T("以下のエントリはフォルダに属していないため、Executeでは処理されません。"
                           + "「Folder ▾」でフォルダへ移動してください。",
                           "The entries below belong to no folder, so Execute skips them. "
                           + "Move them into a folder with \"Folder ▾\"."),
                    MessageType.Warning);

                foreach (int i in visibleRootIndices)
                {
                    DrawEntryListItem(i);
                }
            }

            // フォルダごとのエントリ(空のフォルダも見出しだけ表示する)
            foreach (BakeFolderState folder in settings.bakeFolders)
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
                    if (EntryMatchesSearch(i, normalizedSearch)) visibleIndices.Add(i);
                }

                // 検索中は一致するエントリが無いフォルダを丸ごと隠し、あるフォルダは強制展開する
                if (searching && visibleIndices.Count == 0)
                {
                    continue;
                }

                if (!DrawFolderHeader(folder, indices.Count, searching))
                {
                    continue;
                }

                DrawFolderOutputDirectory(folder);

                // フォルダの中身もドロップ先にする。見出しの細い帯だけが的だと外しやすい。
                Rect bodyRect = EditorGUILayout.BeginHorizontal();
                GUILayout.Space(14);
                EditorGUILayout.BeginVertical();
                foreach (int i in visibleIndices)
                {
                    DrawEntryListItem(i);
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
                HandleFolderDrop(bodyRect, folder.name);
            }

            if (bakeEntriesProp.arraySize == 0)
            {
                EditorGUILayout.LabelField(L10n.T("エントリがありません", "No entries"), EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(selectedEntryIndex <= 0 || selectedEntryIndex >= bakeEntriesProp.arraySize))
            {
                if (GUILayout.Button(new GUIContent("▲", L10n.T("ひとつ上へ", "Move up")),
                        EditorStyles.miniButtonLeft, GUILayout.Width(26)))
                {
                    bakeEntriesProp.MoveArrayElement(selectedEntryIndex, selectedEntryIndex - 1);
                    selectedEntryIndex--;
                    selectedEntryIndices.Clear();
                    selectedEntryIndices.Add(selectedEntryIndex);
                }
            }

            using (new EditorGUI.DisabledScope(selectedEntryIndex < 0 || selectedEntryIndex >= bakeEntriesProp.arraySize - 1))
            {
                if (GUILayout.Button(new GUIContent("▼", L10n.T("ひとつ下へ", "Move down")),
                        EditorStyles.miniButtonRight, GUILayout.Width(26)))
                {
                    bakeEntriesProp.MoveArrayElement(selectedEntryIndex, selectedEntryIndex + 1);
                    selectedEntryIndex++;
                    selectedEntryIndices.Clear();
                    selectedEntryIndices.Add(selectedEntryIndex);
                }
            }

            GUILayout.FlexibleSpace();

            if (selectedEntryIndices.Count > 1)
            {
                EditorGUILayout.LabelField($"{selectedEntryIndices.Count} selected", EditorStyles.miniLabel,
                                           GUILayout.Width(72));
            }

            using (new EditorGUI.DisabledScope(!searching))
            {
                if (GUILayout.Button(new GUIContent(L10n.T("表示中を全選択", "Select Filtered"),
                        L10n.T("検索で絞り込まれている行をまとめて選択します", "Select every row the search leaves visible")),
                    EditorStyles.miniButtonLeft, GUILayout.Width(92)))
                {
                    CheckAllFiltered(normalizedSearch);
                }
            }

            using (new EditorGUI.DisabledScope(selectedEntryIndices.Count == 0))
            {
                if (GUILayout.Button(new GUIContent(L10n.T("解除", "Clear"),
                        L10n.T("選択を解除します", "Clear the selection")),
                    EditorStyles.miniButtonRight, GUILayout.Width(44)))
                {
                    selectedEntryIndices.Clear();
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// エントリ 1 行。
        ///
        /// チェックボックスは「このエントリを焼くか」の 1 つだけ。以前は選択用の
        /// チェックと実行フラグのトグルが並んでいて、どちらが何なのか見て分からなかった。
        /// 一括操作の対象は、リストの選択そのもの（Ctrl / Shift クリック）に寄せた。
        ///
        /// 行はボタンではなく Rect として扱う。修飾キー付きのクリックと、
        /// フォルダへのドラッグを自分で捌く必要があるため。
        /// </summary>
        private void DrawEntryListItem(int i)
        {
            SerializedProperty entryProp = bakeEntriesProp.GetArrayElementAtIndex(i);
            string label = GetEntryLabel(entryProp);

            Rect row = EditorGUILayout.GetControlRect(GUILayout.Height(EntryRowHeight));
            bool isSelected = selectedEntryIndices.Contains(i);

            if (Event.current.type == EventType.Repaint)
            {
                if (isSelected)
                {
                    EditorGUI.DrawRect(row, selectedEntryIndex == i
                        ? new Color(0.24f, 0.42f, 0.68f, 0.85f)
                        : new Color(0.24f, 0.42f, 0.68f, 0.45f));
                }
                else if (row.Contains(Event.current.mousePosition))
                {
                    EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.05f));
                }
            }

            var toggleRect = new Rect(row.x + 2f, row.y + 1f, 16f, row.height - 2f);
            var labelRect = new Rect(toggleRect.xMax + 4f, row.y, row.width - toggleRect.width - 8f, row.height);

            SerializedProperty enabledProp = entryProp.FindPropertyRelative("enabled");
            enabledProp.boolValue = EditorGUI.Toggle(toggleRect, enabledProp.boolValue);

            var content = new GUIContent(label,
                L10n.T("クリックで選択。Ctrlで追加選択、Shiftで範囲選択。ドラッグでフォルダへ移動できます。",
                       "Click to select. Ctrl to add, Shift for a range. Drag onto a folder to move it."));
            GUI.Label(labelRect, content, isSelected ? EditorStyles.whiteLabel : EditorStyles.label);

            HandleEntryRowInput(row, labelRect, i);
        }

        /// <summary>行のクリック・ドラッグ開始・右クリックメニューを捌く。</summary>
        private void HandleEntryRowInput(Rect row, Rect labelRect, int i)
        {
            Event e = Event.current;

            switch (e.type)
            {
                case EventType.MouseDown when labelRect.Contains(e.mousePosition) && e.button == 0:
                    SelectEntry(i, e.control || e.command, e.shift);
                    entryDragStart = e.mousePosition;
                    entryDragCandidate = i;
                    GUI.FocusControl(null);
                    e.Use();
                    break;

                case EventType.MouseDown when row.Contains(e.mousePosition) && e.button == 1:
                    if (!selectedEntryIndices.Contains(i)) SelectEntry(i, false, false);
                    ShowEntryContextMenu();
                    e.Use();
                    break;

                // 少し動いてから掴む。クリックのたびにドラッグが始まると選択できない。
                case EventType.MouseDrag when entryDragCandidate == i
                                              && Vector2.Distance(entryDragStart, e.mousePosition) > 4f:
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.SetGenericData(EntryDragKey, new List<int>(selectedEntryIndices));
                    DragAndDrop.objectReferences = new UnityEngine.Object[0];
                    DragAndDrop.StartDrag(selectedEntryIndices.Count > 1
                        ? $"{selectedEntryIndices.Count} entries"
                        : GetEntryLabel(bakeEntriesProp.GetArrayElementAtIndex(i)));
                    entryDragCandidate = -1;
                    e.Use();
                    break;

                case EventType.MouseUp:
                    entryDragCandidate = -1;
                    break;
            }
        }

        /// <summary>クリックの修飾キーに応じて選択を更新する。</summary>
        private void SelectEntry(int i, bool additive, bool range)
        {
            if (range && IsEntryIndexValid(selectedEntryIndex))
            {
                int from = Mathf.Min(selectedEntryIndex, i);
                int to = Mathf.Max(selectedEntryIndex, i);
                selectedEntryIndices.Clear();
                for (int index = from; index <= to; index++) selectedEntryIndices.Add(index);
                return;
            }

            if (additive)
            {
                if (!selectedEntryIndices.Add(i)) selectedEntryIndices.Remove(i);
                selectedEntryIndex = i;
                return;
            }

            selectedEntryIndices.Clear();
            selectedEntryIndices.Add(i);
            selectedEntryIndex = i;
        }

        /// <summary>右クリックメニュー。フォルダ移動の道はドラッグだけにしない。</summary>
        private void ShowEntryContextMenu()
        {
            var menu = new GenericMenu();
            List<int> targets = GetBatchTargetIndices();

            foreach (string folderName in CollectFolderNames())
            {
                string captured = folderName;
                menu.AddItem(new GUIContent($"{L10n.T("フォルダへ移動", "Move to folder")}/{folderName}"),
                    false, () => AssignFolderToEntries(targets, captured));
            }

            if (settings.bakeFolders == null || settings.bakeFolders.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent(L10n.T("フォルダがありません", "No folders yet")));
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent(L10n.T("フォルダから出す", "Move out of folder")), false,
                () => AssignFolderToEntries(targets, string.Empty));
            menu.ShowAsContext();
        }

        /// <summary>フォルダ見出しへのドロップを受ける。受け取ったら true。</summary>
        private void HandleFolderDrop(Rect rect, string folderName)
        {
            Event e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;

            bool carriesEntries = DragAndDrop.GetGenericData(EntryDragKey) is List<int>;
            if (!carriesEntries) return;

            switch (e.type)
            {
                case EventType.DragUpdated:
                    DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                    e.Use();
                    break;

                case EventType.DragPerform:
                    DragAndDrop.AcceptDrag();
                    AssignFolderToEntries((List<int>)DragAndDrop.GetGenericData(EntryDragKey), folderName);
                    DragAndDrop.SetGenericData(EntryDragKey, null);
                    e.Use();
                    GUIUtility.ExitGUI();
                    break;
            }
        }

        /// <summary>
        /// フォルダ見出し行。折りたたみと「このフォルダをベイクするか」のトグルを持つ。
        /// 戻り値は中身を描画すべきかどうか(検索中は強制展開)。
        /// </summary>
        private bool DrawFolderHeader(BakeFolderState folder, int entryCount, bool searching)
        {
            // BeginHorizontal の戻り値がこの行の矩形。ドロップ判定に使う。
            Rect headerRect = EditorGUILayout.BeginHorizontal("box");

            bool shownExpanded = searching || folder.expanded;
            bool nowExpanded = EditorGUILayout.Foldout(shownExpanded, GUIContent.none, true, EditorStyles.foldoutHeader);
            if (!searching && nowExpanded != folder.expanded)
            {
                folder.expanded = nowExpanded;
                EditorUtility.SetDirty(settings);
            }

            // 名前は見出しでそのまま直せる。作成時に名前を訊くウィンドウを出すのをやめた分、
            // ここが唯一の名前の置き場になる。
            EditorGUI.BeginChangeCheck();
            string renamed = EditorGUILayout.DelayedTextField(folder.name, EditorStyles.boldLabel,
                                                              GUILayout.MinWidth(60));
            if (EditorGUI.EndChangeCheck())
            {
                RenameFolder(folder, renamed);
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.LabelField($"({entryCount})", EditorStyles.miniLabel, GUILayout.Width(30));
            GUILayout.FlexibleSpace();

            // Bake フラグはフォルダ見出しに常時表示して、すぐ切り替えられるようにする
            Color prevBg = GUI.backgroundColor;
            if (!folder.bakeEnabled)
            {
                GUI.backgroundColor = new Color(1f, 0.55f, 0.45f, 0.9f);
            }
            EditorGUI.BeginChangeCheck();
            bool bake = GUILayout.Toggle(folder.bakeEnabled,
                new GUIContent(folder.bakeEnabled ? "Bake: ON" : "Bake: OFF",
                    L10n.T("OFFにすると、このフォルダ内のエントリはExecuteで処理されません",
                           "When OFF, entries in this folder are skipped by Execute")),
                EditorStyles.miniButton, GUILayout.Width(72));
            GUI.backgroundColor = prevBg;
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(settings, "Toggle Folder Bake");
                folder.bakeEnabled = bake;
                EditorUtility.SetDirty(settings);
                settings.SaveSettings();
            }

            if (GUILayout.Button(new GUIContent("✕",
                    L10n.T("フォルダを削除(中のエントリはフォルダなしに戻ります)",
                           "Delete this folder (entries inside are moved out of folders)")),
                EditorStyles.miniButton, GUILayout.Width(20)))
            {
                DeleteFolder(folder, entryCount);
                GUIUtility.ExitGUI(); // リスト構造が変わるので今フレームの描画を打ち切る
            }

            EditorGUILayout.EndHorizontal();

            // 見出しはドロップ先。行をここへ落とすとこのフォルダへ移る。
            HandleFolderDrop(headerRect, folder.name);

            return searching || folder.expanded;
        }

        /// <summary>フォルダ名を変える。中のエントリが持っている所属名も付け替える。</summary>
        private void RenameFolder(BakeFolderState folder, string requested)
        {
            string trimmed = requested?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, folder.name, StringComparison.Ordinal))
            {
                return;
            }

            serializedSettings.ApplyModifiedProperties();

            string unique = string.Equals(trimmed, folder.name, StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : MakeUniqueFolderName(trimmed);

            Undo.RecordObject(settings, "Rename Folder");

            string previous = folder.name;
            folder.name = unique;

            foreach (AnimationBakeEntry entry in settings.bakeEntries)
            {
                if (entry != null && string.Equals(entry.folder?.Trim(), previous, StringComparison.OrdinalIgnoreCase))
                {
                    entry.folder = unique;
                }
            }

            EditorUtility.SetDirty(settings);
            settings.SaveSettings();
            serializedSettings.Update();
        }

        /// <summary>
        /// フォルダの出力先。見出しの直下、エントリの並びより前に置く。
        /// 見出し行へ押し込むと横幅が足りないので、展開したときだけ縦に並べる。
        /// </summary>
        private void DrawFolderOutputDirectory(BakeFolderState folder)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(14);
            EditorGUILayout.BeginVertical();

            EditorGUI.BeginChangeCheck();
            var output = (DefaultAsset)EditorGUILayout.ObjectField(
                new GUIContent("Output Directory",
                    L10n.T("このフォルダの既定の出力先。エントリ側で個別に上書きできます",
                           "Default output folder for this folder. Individual entries can override it")),
                folder.outputDirectory, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                serializedSettings.ApplyModifiedProperties();
                Undo.RecordObject(settings, "Set Folder Output Directory");
                folder.outputDirectory = output;
                EditorUtility.SetDirty(settings);
                serializedSettings.Update();
                settings.SaveSettings();
            }

            // フォルダを指していない DefaultAsset(FBX などを放り込んだ場合)は弾く
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

        private void DrawEntryDetailPane()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            DrawTemplateToolbar();
            entryDetailScrollPosition = EditorGUILayout.BeginScrollView(entryDetailScrollPosition, "box");

            if (!IsEntryIndexValid(selectedEntryIndex))
            {
                EditorGUILayout.LabelField(L10n.T("左のリストからエントリを選択してください", "Select an entry from the list"),
                    EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            SerializedProperty entryProp = bakeEntriesProp.GetArrayElementAtIndex(selectedEntryIndex);

            EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("displayName"), new GUIContent("Name",
                L10n.T("リストに表示される名前(空ならFBX名)", "Name shown in the list (empty = FBX name)")));

            SerializedProperty sourceProp = entryProp.FindPropertyRelative("sourceFbx");
            EditorGUILayout.PropertyField(sourceProp, new GUIContent("Source FBX",
                L10n.T("アニメーションをベイクする対象のFBX", "The FBX the animation is baked onto")));

            if (sourceProp.objectReferenceValue != null)
            {
                string sourcePath = AssetDatabase.GetAssetPath(sourceProp.objectReferenceValue);
                if (!sourcePath.ToLowerInvariant().EndsWith(".fbx"))
                {
                    EditorGUILayout.HelpBox(L10n.T(
                        "Source FBX には .fbx のモデルアセットを指定してください。",
                        "Source FBX should be an .fbx model asset."), MessageType.Warning);
                }
            }

            DrawMotionList(entryProp);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(L10n.T("出力", "Output"), EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("outputDirectoryOverride"), new GUIContent("Output Override",
                L10n.T("このエントリだけ別のフォルダへ出力する場合に設定", "Per-entry output folder override")));
            EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("outputFileName"), new GUIContent("Output File Name",
                L10n.T("拡張子なしのファイル名(空ならクリップ名。複数クリップならクリップ名を後置)",
                       "File name without extension (empty = clip name; the clip name is appended when there are multiple clips)")));
            SerializedProperty exportContentProp = entryProp.FindPropertyRelative("exportContent");
            EditorGUILayout.PropertyField(exportContentProp, new GUIContent("Export Content",
                L10n.T("生成FBXに含めるもの。Skeleton Onlyはメッシュ/レンダラーを外し、アニメーションするノード階層だけにします",
                       "What to include in the generated FBX. Skeleton Only strips meshes/renderers and keeps only the animated node hierarchy")));

            if (exportContentProp.enumValueIndex == (int)BakeExportContent.ModelAndAnimation)
            {
                EditorGUILayout.HelpBox(L10n.T(
                    "モデル込みで書き出すため、メッシュやブレンドシェイプの分だけFBXが大きくなります。アニメーションだけが欲しい場合は Skeleton Only を選んでください。",
                    "Exporting the model includes meshes and blend shapes, which makes the FBX large. Choose Skeleton Only if you only need the animation."),
                    MessageType.Info);
            }

            EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("importAnimationType"), new GUIContent("Import Animation Type",
                L10n.T("生成したFBXを読み込み直すときのAnimation Type", "Animation Type applied when the generated FBX is imported back")));
            EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("fastImport"), new GUIContent("Fast Import",
                L10n.T("生成FBXのインポート時に不要な処理(マテリアル/カメラ/ライト/タンジェント計算等)を省いて速くする。結果が変わる項目には触れません",
                       "Skip import work the baked FBX does not need (materials, cameras, lights, tangents). Never touches anything that changes the result")));
            EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("saveBakedClipAsset"), new GUIContent("Save Baked .anim",
                L10n.T("ベイク済みTransformクリップを .anim としても保存する", "Also save the baked Transform clip as a .anim asset")));
            EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("exportAscii"), new GUIContent("Export ASCII",
                L10n.T("バイナリではなくASCII形式のFBXで書き出す", "Export ASCII FBX instead of binary")));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(L10n.T("ベイク設定", "Bake Settings"), EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("frameRate"), new GUIContent("Frame Rate",
                L10n.T("サンプリングのフレームレート(0で元クリップのフレームレート)",
                       "Sampling frame rate (0 = source clip frame rate)")));
            EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("bakeRootMotion"), new GUIContent("Bake Root Motion",
                L10n.T("ルートモーションを含める。焼き込み先は常に Hips で、モデルのオブジェクト自体にはキーを打ちません。OFFでその場での動きになります",
                       "Include root motion. It is baked into the Hips bone, never onto the model object itself. Turn it off for an in-place motion")));
            EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("bakeScale"), new GUIContent("Bake Scale",
                L10n.T("スケールカーブもベイクする", "Bake Transform scale curves as well")));
            SerializedProperty bakeBlendShapesProp = entryProp.FindPropertyRelative("bakeBlendShapes");
            EditorGUILayout.PropertyField(bakeBlendShapesProp, new GUIContent("Bake BlendShapes",
                L10n.T("クリップが動かすブレンドシェイプもベイクする", "Bake blend shape weights driven by the clip")));

            using (new EditorGUI.DisabledScope(bakeBlendShapesProp.boolValue))
            {
                EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("excludeBlendShapes"), new GUIContent("Exclude BlendShapes",
                    L10n.T("メッシュからブレンドシェイプを取り除いて書き出す(FBXの容量削減に一番効きます)",
                           "Strip blend shape data from the exported meshes (usually the biggest size win)")));
            }
            EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("removeConstantCurves"), new GUIContent("Remove Constant Curves",
                L10n.T("値が変化しないカーブを省いてFBXを軽くする(1Fポーズなど全カーブが定数の場合は自動的に無効化されます)",
                       "Drop curves whose value never changes (automatically disabled when every curve is constant, e.g. a 1-frame pose)")));

            SerializedProperty reductionProp = entryProp.FindPropertyRelative("keyframeReduction");
            EditorGUILayout.PropertyField(reductionProp, new GUIContent("Keyframe Reduction",
                L10n.T("直線上に乗るキーを間引いてFBXを軽くする", "Remove keys that sit on a straight line between their neighbours")));
            using (new EditorGUI.DisabledScope(!reductionProp.boolValue))
            {
                EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("reductionTolerance"), new GUIContent("Reduction Tolerance",
                    L10n.T("間引きの許容誤差。大きいほど軽くなりますが精度は落ちます",
                           "Allowed error for keyframe reduction. Larger = smaller file, less accurate")));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Avatar", EditorStyles.boldLabel);

            SerializedProperty useOtherAvatarProp = entryProp.FindPropertyRelative("useOtherAvatarDefinition");
            EditorGUILayout.PropertyField(useOtherAvatarProp, new GUIContent("Use Other Avatar Definition",
                L10n.T("サンプリング時にFBX以外のAvatarを使う", "Use an Avatar other than the one from the source FBX")));
            using (new EditorGUI.DisabledScope(!useOtherAvatarProp.boolValue))
            {
                EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("avatarDefinition"), new GUIContent("Avatar Definition"));
            }

            DrawOutputPreview();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 焼くモーションの一覧。
        ///
        /// AnimationClip と .bvh を同じ一覧に置く。以前は「Humanoid Clips」と
        /// 「BVH File」で欄が分かれていて、このエントリが結局どちらを変換するのか
        /// 見て分からなかった。1 行 1 モーション、1 モーションにつき FBX が 1 つ。
        /// </summary>
        private void DrawMotionList(SerializedProperty entryProp)
        {
            SerializedProperty motionsProp = entryProp.FindPropertyRelative("motions");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent(L10n.T("モーション", "Motion"),
                    L10n.T("焼くモーション。AnimationClip(.anim / FBX内蔵)と .bvh を混ぜて置けます。1つにつきFBXを1つ出力します",
                           "Motions to bake. Animation clips and .bvh files can be mixed. One FBX per motion")),
                EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(24)))
            {
                motionsProp.InsertArrayElementAtIndex(motionsProp.arraySize);
                motionsProp.GetArrayElementAtIndex(motionsProp.arraySize - 1).objectReferenceValue = null;
            }
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < motionsProp.arraySize; i++)
            {
                SerializedProperty element = motionsProp.GetArrayElementAtIndex(i);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(DescribeMotion(element.objectReferenceValue),
                                           EditorStyles.miniLabel, GUILayout.Width(40));
                element.objectReferenceValue = EditorGUILayout.ObjectField(
                    element.objectReferenceValue, typeof(UnityEngine.Object), false);

                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(24)))
                {
                    motionsProp.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            DrawMotionDropArea(motionsProp);

            bool hasBvh = false;
            for (int i = 0; i < motionsProp.arraySize; i++)
            {
                UnityEngine.Object motion = motionsProp.GetArrayElementAtIndex(i).objectReferenceValue;
                if (motion == null) continue;

                if (IsBvhAsset(motion)) { hasBvh = true; continue; }
                if (motion is AnimationClip) continue;

                EditorGUILayout.HelpBox(L10n.T(
                    $"\"{motion.name}\" は AnimationClip でも .bvh でもないため、Executeで無視されます。",
                    $"\"{motion.name}\" is neither an AnimationClip nor a .bvh file, so Execute skips it."),
                    MessageType.Warning);
            }

            if (hasBvh)
            {
                DrawBvhOptions(entryProp);
            }
        }

        /// <summary>一覧の下の受け皿。まとめて放り込めるようにしておく。</summary>
        private void DrawMotionDropArea(SerializedProperty motionsProp)
        {
            Rect drop = GUILayoutUtility.GetRect(0f, 28f, GUILayout.ExpandWidth(true));
            GUI.Box(drop, L10n.T("ここへ .anim / FBX / .bvh をドロップ", "Drop .anim / FBX / .bvh here"),
                    EditorStyles.helpBox);

            Event e = Event.current;
            if (!drop.Contains(e.mousePosition)) return;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type != EventType.DragPerform) return;

            DragAndDrop.AcceptDrag();
            foreach (UnityEngine.Object dropped in DragAndDrop.objectReferences)
            {
                // FBX を落としたら、その中のクリップを取り出す。モデルそのものを
                // モーションとして扱っても意味が無い。
                if (dropped is GameObject)
                {
                    foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(dropped)))
                    {
                        if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) AddMotion(motionsProp, clip);
                    }
                    continue;
                }

                if (dropped is AnimationClip || IsBvhAsset(dropped)) AddMotion(motionsProp, dropped);
            }
            e.Use();
        }

        private static void AddMotion(SerializedProperty motionsProp, UnityEngine.Object motion)
        {
            for (int i = 0; i < motionsProp.arraySize; i++)
            {
                if (motionsProp.GetArrayElementAtIndex(i).objectReferenceValue == motion) return;
            }
            motionsProp.InsertArrayElementAtIndex(motionsProp.arraySize);
            motionsProp.GetArrayElementAtIndex(motionsProp.arraySize - 1).objectReferenceValue = motion;
        }

        internal static bool IsBvhAsset(UnityEngine.Object asset)
        {
            if (asset == null) return false;
            string path = AssetDatabase.GetAssetPath(asset);
            return !string.IsNullOrEmpty(path) && path.EndsWith(".bvh", StringComparison.OrdinalIgnoreCase);
        }

        private static string DescribeMotion(UnityEngine.Object motion)
        {
            if (motion == null) return string.Empty;
            if (IsBvhAsset(motion)) return "BVH";
            return motion is AnimationClip ? "Clip" : "?";
        }

        /// <summary>
        /// BVH が一覧に入っているときだけ出す設定。
        /// クリップしか無いエントリに BVH の設定が並んでいても邪魔なだけ。
        /// </summary>
        private void DrawBvhOptions(SerializedProperty entryProp)
        {
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(L10n.T("BVH の読み方", "BVH options"), EditorStyles.miniBoldLabel);

            EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("bvhUpAxis"), new GUIContent("Up Axis",
                L10n.T("BVHがどの軸を上としているか。Autoは骨格のOFFSETから判定します(規格はYですが実際にはZも多い)",
                       "Which axis the BVH treats as up. Auto reads it from the skeleton's offsets")));

            EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("bvhScale"), new GUIContent("BVH Scale",
                L10n.T("BVHの単位換算。リターゲットはHumanoidの正規化を通るため、一様な拡大縮小は結果に影響しません(Avatarが組めないほど極端なときだけ触ってください)",
                       "Unit conversion. Retargeting goes through humanoid normalisation, so a uniform scale does not change the result")));

            EditorGUILayout.HelpBox(L10n.T(
                "BVHの骨格にもHumanoid Avatarを組み、そのポーズをSource FBXへ流します。"
                + "そのためSource FBX側のRigもHumanoidである必要があります。",
                "A humanoid Avatar is built for the BVH skeleton and its pose is pushed onto the Source FBX, "
                + "so the Source FBX rig must be Humanoid too."),
                MessageType.Info);

            DrawBvhBoneOverrides(entryProp);
        }

        /// <summary>
        /// ジョイント名の自動推測を上書きする表。
        /// 自動で当たった分も「推測を確認」で一覧に出さないと、何が外れているのか分からない。
        /// </summary>
        private void DrawBvhBoneOverrides(SerializedProperty entryProp)
        {
            SerializedProperty overridesProp = entryProp.FindPropertyRelative("bvhBoneOverrides");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(L10n.T("ボーン対応の手当て", "Bone Overrides"), EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();

            string bvhPath = FirstBvhPath(entryProp);
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(bvhPath)))
            {
                if (GUILayout.Button(new GUIContent(L10n.T("推測を確認", "Check Mapping"),
                        L10n.T("BVHを読んで、どのジョイントがどのHumanoidボーンに当たったかをConsoleへ出します",
                               "Read the BVH and log which joint mapped to which humanoid bone")),
                    EditorStyles.miniButton, GUILayout.Width(90)))
                {
                    LogBvhBoneMapping(entryProp, bvhPath);
                }
            }

            if (GUILayout.Button(new GUIContent("+", L10n.T("手当てを1件追加", "Add one override")),
                    EditorStyles.miniButton, GUILayout.Width(24)))
            {
                overridesProp.InsertArrayElementAtIndex(overridesProp.arraySize);
            }
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < overridesProp.arraySize; i++)
            {
                SerializedProperty element = overridesProp.GetArrayElementAtIndex(i);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(element.FindPropertyRelative("jointName"), GUIContent.none);
                EditorGUILayout.LabelField("→", GUILayout.Width(16));
                EditorGUILayout.PropertyField(element.FindPropertyRelative("humanBoneName"), GUIContent.none);

                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(24)))
                {
                    overridesProp.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (overridesProp.arraySize == 0)
            {
                EditorGUILayout.LabelField(L10n.T(
                    "空でよければ自動推測だけで動きます。",
                    "Leave this empty to rely on the automatic guess."), EditorStyles.miniLabel);
            }
        }

        /// <summary>一覧に入っている最初の .bvh のパス。無ければ空。</summary>
        private static string FirstBvhPath(SerializedProperty entryProp)
        {
            SerializedProperty motionsProp = entryProp.FindPropertyRelative("motions");
            for (int i = 0; i < motionsProp.arraySize; i++)
            {
                UnityEngine.Object motion = motionsProp.GetArrayElementAtIndex(i).objectReferenceValue;
                if (IsBvhAsset(motion)) return AssetDatabase.GetAssetPath(motion);
            }
            return string.Empty;
        }

        /// <summary>「推測を確認」の中身。読めない BVH はここで分かる。</summary>
        private void LogBvhBoneMapping(SerializedProperty entryProp, string bvhPath)
        {
            try
            {
                string absolute = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                    System.IO.Directory.GetParent(Application.dataPath).FullName, bvhPath));

                Bvh.BvhFile file = Bvh.BvhFile.Load(absolute);

                var overrides = new List<Bvh.BvhBoneOverride>();
                SerializedProperty overridesProp = entryProp.FindPropertyRelative("bvhBoneOverrides");
                for (int i = 0; i < overridesProp.arraySize; i++)
                {
                    SerializedProperty element = overridesProp.GetArrayElementAtIndex(i);
                    overrides.Add(new Bvh.BvhBoneOverride
                    {
                        jointName = element.FindPropertyRelative("jointName").stringValue,
                        humanBoneName = element.FindPropertyRelative("humanBoneName").stringValue,
                    });
                }

                Dictionary<string, string> map = Bvh.BvhHumanoid.BuildBoneMap(file, overrides);

                var lines = new System.Text.StringBuilder();
                lines.AppendLine($"{LogPrefix} {System.IO.Path.GetFileName(bvhPath)}: "
                                 + $"{file.Frames.Count} frame(s) @ {file.FrameRate:F2} fps, up axis = {file.GuessUpAxis()}");
                lines.AppendLine(Bvh.BvhHumanoid.DescribeBoneMap(file, map));
                foreach (KeyValuePair<string, string> pair in map)
                {
                    lines.AppendLine($"  {pair.Key} → {pair.Value}");
                }

                Debug.Log(lines.ToString());
            }
            catch (System.Exception e)
            {
                Debug.LogError($"{LogPrefix} BVH を読めませんでした: {e.Message}");
            }
        }

        /// <summary>実行前に、どのパスへ書き出されるかを確認できるようにしておく。</summary>
        private void DrawOutputPreview()
        {
            // 直前の編集がまだ ApplyModifiedProperties されていない場合、
            // 実体側(settings.bakeEntries)の要素数が SerializedProperty とずれることがある。
            if (settings.bakeEntries == null || selectedEntryIndex < 0 || selectedEntryIndex >= settings.bakeEntries.Count)
            {
                return;
            }

            AnimationBakeEntry entry = settings.bakeEntries[selectedEntryIndex];
            if (entry == null)
            {
                return;
            }

            var motionNames = new List<string>();
            if (entry.motions != null)
            {
                foreach (UnityEngine.Object motion in entry.motions)
                {
                    if (motion == null) continue;
                    motionNames.Add(motion is AnimationClip ? motion.name
                                                            : System.IO.Path.GetFileNameWithoutExtension(
                                                                  AssetDatabase.GetAssetPath(motion)));
                }
            }
            bool multiOutput = motionNames.Count > 1;

            if (motionNames.Count == 0)
            {
                return;
            }

            string folder = GetEntryOutputFolder(entry);
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(L10n.T("出力プレビュー", "Output Preview"), EditorStyles.boldLabel);

            foreach (string motionName in motionNames)
            {
                EditorGUILayout.LabelField($"{folder}/{GetOutputName(entry, motionName, multiOutput)}.fbx", EditorStyles.miniLabel);
            }
        }

        private void DrawExecuteBar()
        {
            // 出力先が揃った有効なフォルダが 1 つでもあれば実行できる。
            bool isValid = FbxExporterBridge.IsAvailable
                && CountExecutableFolders() > 0
                && bakeEntriesProp.arraySize > 0;

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(!isValid))
            {
                if (GUILayout.Button("Execute", GUILayout.Height(34)))
                {
                    serializedSettings.ApplyModifiedProperties();
                    ProcessBakeEntries();
                }

                if (GUILayout.Button(new GUIContent("Re-bake All",
                        L10n.T("差分キャッシュを無視し、全エントリを強制的に再ベイクします",
                               "Ignore the diff cache and force a full re-bake of every entry")),
                    GUILayout.Height(34), GUILayout.Width(140)))
                {
                    bool confirmed = EditorUtility.DisplayDialog(
                        L10n.T("全再ベイク", "Re-bake All"),
                        L10n.T(
                            "キャッシュを無視して全エントリを再ベイクします。\nクリップ数が多いと時間がかかります。実行しますか?",
                            "Ignore the cache and re-bake every entry.\nThis can take a while for many clips. Continue?"),
                        L10n.T("実行", "OK"),
                        L10n.T("キャンセル", "Cancel"));

                    if (confirmed)
                    {
                        serializedSettings.ApplyModifiedProperties();
                        ProcessBakeEntries(true);
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        // ═══════════════════════════════════════════════════════════════
        //  リスト操作
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 新しく作るエントリの所属先。選択中エントリのフォルダを引き継ぎ、
        /// 何も選んでいなければ最初のフォルダへ入れる。
        /// </summary>
        private string CurrentFolderForNewEntry()
        {
            if (IsEntryIndexValid(selectedEntryIndex))
            {
                string folder = GetEntryFolderName(selectedEntryIndex);
                if (!string.IsNullOrEmpty(folder)) return folder;
            }

            List<string> names = CollectFolderNames();
            return names.Count > 0 ? names[0] : string.Empty;
        }

        private bool IsEntryIndexValid(int index)
        {
            return index >= 0 && index < bakeEntriesProp.arraySize;
        }

        private void EnsureSelectedEntryIndex()
        {
            if (bakeEntriesProp.arraySize == 0)
            {
                selectedEntryIndex = -1;
                return;
            }

            selectedEntryIndex = Mathf.Clamp(selectedEntryIndex, 0, bakeEntriesProp.arraySize - 1);
        }

        private void AddEntry(GameObject sourceFbx, IEnumerable<AnimationClip> clips)
        {
            int newIndex = bakeEntriesProp.arraySize;
            bakeEntriesProp.InsertArrayElementAtIndex(newIndex);
            SerializedProperty entryProp = bakeEntriesProp.GetArrayElementAtIndex(newIndex);

            // InsertArrayElementAtIndex は直前の要素のコピーを作るため、既定値へ戻す
            entryProp.FindPropertyRelative("displayName").stringValue = string.Empty;
            entryProp.FindPropertyRelative("enabled").boolValue = true;
            // 追加したエントリは、今選んでいるエントリと同じフォルダへ入れる。
            // フォルダごとに出力先が違うので、フォルダ無しで生まれると必ず移動が要る。
            entryProp.FindPropertyRelative("folder").stringValue = CurrentFolderForNewEntry();
            entryProp.FindPropertyRelative("sourceFbx").objectReferenceValue = sourceFbx;
            entryProp.FindPropertyRelative("outputDirectoryOverride").objectReferenceValue = null;
            entryProp.FindPropertyRelative("outputFileName").stringValue = string.Empty;
            entryProp.FindPropertyRelative("useOtherAvatarDefinition").boolValue = false;
            entryProp.FindPropertyRelative("avatarDefinition").objectReferenceValue = null;
            entryProp.FindPropertyRelative("frameRate").floatValue = 0f;
            entryProp.FindPropertyRelative("bakeRootMotion").boolValue = true;
            entryProp.FindPropertyRelative("bakeScale").boolValue = false;
            entryProp.FindPropertyRelative("bakeBlendShapes").boolValue = false;
            entryProp.FindPropertyRelative("excludeBlendShapes").boolValue = false;
            entryProp.FindPropertyRelative("removeConstantCurves").boolValue = true;
            entryProp.FindPropertyRelative("keyframeReduction").boolValue = true;
            entryProp.FindPropertyRelative("reductionTolerance").floatValue = 0.0001f;
            entryProp.FindPropertyRelative("saveBakedClipAsset").boolValue = true;
            entryProp.FindPropertyRelative("fastImport").boolValue = true;
            entryProp.FindPropertyRelative("exportAscii").boolValue = false;
            entryProp.FindPropertyRelative("exportContent").enumValueIndex = (int)BakeExportContent.ModelAndAnimation;
            entryProp.FindPropertyRelative("importAnimationType").enumValueIndex = (int)BakedFbxAnimationType.Generic;

            SerializedProperty motionsProp = entryProp.FindPropertyRelative("motions");
            motionsProp.ClearArray();
            if (clips != null)
            {
                foreach (AnimationClip clip in clips)
                {
                    if (clip != null) AddMotion(motionsProp, clip);
                }
            }

            selectedEntryIndex = newIndex;
        }

        /// <summary>
        /// Project ウィンドウの選択から、FBX 1 つにつき 1 エントリを作る。
        /// 選択中の AnimationClip(FBX 内蔵クリップを含む)は全エントリに割り当てる。
        /// </summary>
        private void AddEntriesFromSelection()
        {
            var fbxObjects = new List<GameObject>();
            var clips = new List<AnimationClip>();

            foreach (UnityEngine.Object selected in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(selected);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                if (selected is AnimationClip clip)
                {
                    clips.Add(clip);
                }
                else if (selected is GameObject go && path.ToLowerInvariant().EndsWith(".fbx"))
                {
                    fbxObjects.Add(go);
                }
            }

            if (fbxObjects.Count == 0)
            {
                Debug.LogWarning($"{LogPrefix} No FBX model is selected in the Project window.");
                return;
            }

            foreach (GameObject fbx in fbxObjects)
            {
                AddEntry(fbx, clips);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  フォルダ
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Output Directory が有効なフォルダを指していて、Bake が ON のフォルダ数。</summary>
        private int CountExecutableFolders()
        {
            if (settings?.bakeFolders == null) return 0;

            int count = 0;
            foreach (BakeFolderState folder in settings.bakeFolders)
            {
                if (folder == null || string.IsNullOrWhiteSpace(folder.name) || !folder.bakeEnabled) continue;
                if (!IsValidFolderAsset(folder.outputDirectory)) continue;
                count++;
            }
            return count;
        }

        internal static bool IsValidFolderAsset(UnityEngine.Object folderAsset)
        {
            if (folderAsset == null) return false;
            string path = AssetDatabase.GetAssetPath(folderAsset);
            return !string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path);
        }

        /// <summary>
        /// settings.bakeFolders を整える。フォルダは New Folder ボタンで明示的に作成/削除するため
        /// ここでは削除しない。名前が空・重複のエントリの除去と、エントリ側だけに存在する
        /// フォルダ名(旧データ等)の補完のみ行う。
        /// </summary>
        private void SyncBakeFolders()
        {
            if (settings.bakeFolders == null)
            {
                settings.bakeFolders = new List<BakeFolderState>();
            }

            bool changed = false;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = settings.bakeFolders.Count - 1; i >= 0; i--)
            {
                BakeFolderState folder = settings.bakeFolders[i];
                if (folder == null || string.IsNullOrWhiteSpace(folder.name))
                {
                    settings.bakeFolders.RemoveAt(i);
                    changed = true;
                }
            }
            for (int i = 0; i < settings.bakeFolders.Count; i++)
            {
                if (!seen.Add(settings.bakeFolders[i].name.Trim()))
                {
                    settings.bakeFolders.RemoveAt(i);
                    i--;
                    changed = true;
                }
            }

            if (settings.bakeEntries != null)
            {
                foreach (AnimationBakeEntry entry in settings.bakeEntries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.folder)) continue;
                    string name = entry.folder.Trim();
                    if (seen.Add(name))
                    {
                        settings.bakeFolders.Add(new BakeFolderState { name = name });
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(settings);
            }
        }

        private string GetEntryFolderName(int index)
        {
            SerializedProperty folderProp = bakeEntriesProp.GetArrayElementAtIndex(index).FindPropertyRelative("folder");
            return folderProp == null || string.IsNullOrWhiteSpace(folderProp.stringValue)
                ? string.Empty
                : folderProp.stringValue.Trim();
        }

        /// <summary>作成済みフォルダの名前一覧(重複なし・定義順)を返す。</summary>
        private List<string> CollectFolderNames()
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (settings?.bakeFolders == null) return names;

            foreach (BakeFolderState folder in settings.bakeFolders)
            {
                if (folder == null || string.IsNullOrWhiteSpace(folder.name)) continue;
                string name = folder.name.Trim();
                if (seen.Add(name)) names.Add(name);
            }
            return names;
        }

        /// <summary>登録済みフォルダを名前(大文字小文字無視)で探す。</summary>
        internal BakeFolderState FindFolderState(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName) || settings?.bakeFolders == null)
            {
                return null;
            }

            string key = folderName.Trim();
            foreach (BakeFolderState folder in settings.bakeFolders)
            {
                if (folder != null && string.Equals(folder.name?.Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    return folder;
                }
            }
            return null;
        }


        /// <summary>
        /// フォルダを作成する。同名(大文字小文字無視)が既にあればそれを使う。
        /// assignEntryIndices が指定されていれば、そのエントリをフォルダへ移動する。
        /// </summary>
        private void CreateFolder(string folderName, List<int> assignEntryIndices)
        {
            folderName = folderName?.Trim();
            if (string.IsNullOrEmpty(folderName)) return;

            BakeFolderState existing = FindFolderState(folderName);
            if (existing == null)
            {
                Undo.RecordObject(settings, "Create Bake Folder");
                settings.bakeFolders.Add(new BakeFolderState { name = folderName });
                EditorUtility.SetDirty(settings);
                settings.SaveSettings();
                Debug.Log($"{LogPrefix} Created folder \"{folderName}\".");
            }
            else
            {
                // 既存フォルダ名で作成した場合はそこへ合流させる(見た目の名前は既存側を維持)
                folderName = existing.name.Trim();
            }

            if (assignEntryIndices != null && assignEntryIndices.Count > 0)
            {
                AssignFolderToEntries(assignEntryIndices, folderName);
            }
            else
            {
                Repaint();
            }
        }

        private void AssignFolderToEntries(List<int> entryIndices, string folderName)
        {
            if (entryIndices == null || entryIndices.Count == 0) return;

            serializedSettings.ApplyModifiedProperties();
            Undo.RecordObject(settings, "Set Entry Folder");

            int applied = 0;
            foreach (int i in entryIndices)
            {
                if (i < 0 || i >= settings.bakeEntries.Count) continue;
                AnimationBakeEntry entry = settings.bakeEntries[i];
                if (entry == null) continue;
                entry.folder = folderName;
                applied++;
            }

            EditorUtility.SetDirty(settings);
            serializedSettings.Update();
            settings.SaveSettings();
            Repaint();

            string label = string.IsNullOrEmpty(folderName) ? "(No Folder)" : folderName;
            Debug.Log($"{LogPrefix} Moved {applied} entry/entries to folder \"{label}\".");
        }

        /// <summary>フォルダを削除する。中にエントリがある場合は確認のうえ、フォルダなしへ戻す。</summary>
        private void DeleteFolder(BakeFolderState folder, int entryCount)
        {
            if (entryCount > 0)
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    L10n.T("フォルダ削除", "Delete Folder"),
                    L10n.T(
                        $"フォルダ \"{folder.name}\" を削除します。\n中の {entryCount} 個のエントリは削除されず、フォルダなしに戻ります。よろしいですか?",
                        $"Delete folder \"{folder.name}\"?\nThe {entryCount} entry/entries inside are kept and moved out of the folder."),
                    L10n.T("削除", "Delete"),
                    L10n.T("キャンセル", "Cancel"));
                if (!confirmed) return;
            }

            serializedSettings.ApplyModifiedProperties();
            Undo.RecordObject(settings, "Delete Bake Folder");

            string key = folder.name.Trim();
            if (settings.bakeEntries != null)
            {
                foreach (AnimationBakeEntry entry in settings.bakeEntries)
                {
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.folder)
                        && string.Equals(entry.folder.Trim(), key, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.folder = string.Empty;
                    }
                }
            }
            settings.bakeFolders.Remove(folder);

            EditorUtility.SetDirty(settings);
            serializedSettings.Update();
            settings.SaveSettings();
            Repaint();
        }

        // ═══════════════════════════════════════════════════════════════
        //  チェックによる一括選択
        // ═══════════════════════════════════════════════════════════════

        /// <summary>一括操作の対象。チェックが 1 つも無ければ選択中エントリ 1 つ。</summary>
        private List<int> GetBatchTargetIndices()
        {
            var list = new List<int>();
            if (bakeEntriesProp.arraySize == 0) return list;

            if (selectedEntryIndices.Count > 0)
            {
                foreach (int i in selectedEntryIndices)
                {
                    if (i >= 0 && i < bakeEntriesProp.arraySize) list.Add(i);
                }
            }
            else if (IsEntryIndexValid(selectedEntryIndex))
            {
                list.Add(selectedEntryIndex);
            }
            list.Sort();
            return list;
        }

        private void CheckAllFiltered(string normalizedSearch)
        {
            for (int i = 0; i < bakeEntriesProp.arraySize; i++)
            {
                if (EntryMatchesSearch(i, normalizedSearch))
                {
                    selectedEntryIndices.Add(i);
                }
            }
        }

        private bool EntryMatchesSearch(int index, string normalizedSearch)
        {
            if (string.IsNullOrEmpty(normalizedSearch))
            {
                return true;
            }

            string label = GetEntryLabel(bakeEntriesProp.GetArrayElementAtIndex(index));
            return label.ToLowerInvariant().IndexOf(normalizedSearch, StringComparison.Ordinal) >= 0;
        }

        // ═══════════════════════════════════════════════════════════════
        //  フォルダ名の入力ダイアログ
        // ═══════════════════════════════════════════════════════════════


        private static string GetEntryLabel(SerializedProperty entryProp)
        {
            string displayName = entryProp.FindPropertyRelative("displayName").stringValue;
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                return displayName.Trim();
            }

            UnityEngine.Object fbx = entryProp.FindPropertyRelative("sourceFbx").objectReferenceValue;
            if (fbx != null)
            {
                int motionCount = entryProp.FindPropertyRelative("motions").arraySize;
                return motionCount > 1 ? $"{fbx.name} ({motionCount})" : fbx.name;
            }

            return "(no FBX)";
        }
    }
}
