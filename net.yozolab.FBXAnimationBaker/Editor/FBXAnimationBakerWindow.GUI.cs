using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;

namespace YozoLab.FBXAnimationBaker
{
    /// <summary>
    /// FBXAnimationBakerWindow の GUI 描画担当。OnGUI から各ペインの描画までをここにまとめる。
    /// </summary>
    public partial class FBXAnimationBakerWindow
    {
        private const float EntryListWidth = 240f;

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
            if (GUILayout.Button(L10n.IsEnglish ? "EN" : "JP", GUILayout.Width(35)))
            {
                L10n.IsEnglish = !L10n.IsEnglish;
                Repaint();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            EditorGUILayout.LabelField(L10n.T(
                "FBX と Humanoid AnimationClip を指定すると、クリップを Transform アニメーションとしてベイクした FBX を書き出します。",
                "Pick an FBX and humanoid animation clips to export an FBX with the clip baked as Transform animation."),
                EditorStyles.wordWrappedMiniLabel);

            if (!FbxExporterBridge.IsAvailable)
            {
                EditorGUILayout.HelpBox(L10n.T(
                    "Unity FBX Exporter (com.unity.formats.fbx) が見つかりません。Package Manager からインストールしてください。",
                    "Unity FBX Exporter (com.unity.formats.fbx) was not found. Install it from Package Manager."),
                    MessageType.Error);

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(L10n.T("再チェック", "Re-check"), GUILayout.Width(110)))
                {
                    FbxExporterBridge.ClearCache();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.PropertyField(outputDirectoryProp, new GUIContent("Output Directory",
                L10n.T("生成した FBX の保存先フォルダ", "Output folder for the generated FBX files")));

            if (outputDirectoryProp.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox(L10n.T("Output Directoryを設定してください。", "Please set Output Directory."), MessageType.Warning);
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
            DrawEntryToolbar();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            DrawEntryListPane();
            DrawEntryDetailPane();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawEntryToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Entries: {bakeEntriesProp.arraySize}", EditorStyles.boldLabel, GUILayout.Width(90));
            GUILayout.FlexibleSpace();

            GUILayout.Label("Search", GUILayout.Width(45));
            entrySearchText = EditorGUILayout.TextField(entrySearchText, GUILayout.Width(160));

            if (GUILayout.Button("Add", GUILayout.Width(60)))
            {
                AddEntry(null, null);
            }

            if (GUILayout.Button(new GUIContent("From Selection",
                    L10n.T("Projectで選択中のFBXとAnimationClipからエントリを作成します",
                           "Create entries from the FBX models and animation clips selected in the Project window")),
                GUILayout.Width(110)))
            {
                AddEntriesFromSelection();
            }

            using (new EditorGUI.DisabledScope(!IsEntryIndexValid(selectedEntryIndex)))
            {
                if (GUILayout.Button("Duplicate", GUILayout.Width(80)))
                {
                    bakeEntriesProp.InsertArrayElementAtIndex(selectedEntryIndex);
                    selectedEntryIndex++;

                    SerializedProperty nameProp = bakeEntriesProp
                        .GetArrayElementAtIndex(selectedEntryIndex)
                        .FindPropertyRelative("displayName");
                    if (!string.IsNullOrWhiteSpace(nameProp.stringValue))
                    {
                        nameProp.stringValue = $"{nameProp.stringValue}_copy";
                    }
                }

                if (GUILayout.Button("Delete", GUILayout.Width(70)))
                {
                    bakeEntriesProp.DeleteArrayElementAtIndex(selectedEntryIndex);
                    selectedEntryIndex = Mathf.Clamp(selectedEntryIndex, 0, bakeEntriesProp.arraySize - 1);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawEntryListPane()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(EntryListWidth), GUILayout.ExpandHeight(true));
            entryListScrollPosition = EditorGUILayout.BeginScrollView(entryListScrollPosition, "box");

            bool hasSearch = !string.IsNullOrWhiteSpace(entrySearchText);
            int visibleCount = 0;

            for (int i = 0; i < bakeEntriesProp.arraySize; i++)
            {
                SerializedProperty entryProp = bakeEntriesProp.GetArrayElementAtIndex(i);
                string label = GetEntryLabel(entryProp);

                if (hasSearch && label.IndexOf(entrySearchText.Trim(), System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                visibleCount++;

                EditorGUILayout.BeginHorizontal();

                SerializedProperty enabledProp = entryProp.FindPropertyRelative("enabled");
                enabledProp.boolValue = EditorGUILayout.Toggle(enabledProp.boolValue, GUILayout.Width(16));

                bool isSelected = i == selectedEntryIndex;
                GUIStyle style = isSelected ? EditorStyles.miniButtonMid : EditorStyles.label;
                if (GUILayout.Button(new GUIContent(label, label), style, GUILayout.ExpandWidth(true)))
                {
                    selectedEntryIndex = i;
                    GUI.FocusControl(null);
                }

                EditorGUILayout.EndHorizontal();
            }

            if (bakeEntriesProp.arraySize == 0)
            {
                EditorGUILayout.LabelField(L10n.T("エントリがありません", "No entries"), EditorStyles.centeredGreyMiniLabel);
            }
            else if (visibleCount == 0)
            {
                EditorGUILayout.LabelField(L10n.T("検索に一致しません", "No matches"), EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawEntryDetailPane()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
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

            EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("clips"), new GUIContent("Humanoid Clips",
                L10n.T("ベイクするHumanoidアニメーションクリップ(1クリップにつきFBXを1つ出力)",
                       "Humanoid animation clips to bake (one FBX per clip)")), true);

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
                L10n.T("ルートモーションをルートTransformにベイクする", "Bake root motion into the root Transform")));
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
            if (entry == null || entry.clips == null)
            {
                return;
            }

            List<AnimationClip> clips = entry.clips.Where(c => c != null).ToList();
            if (clips.Count == 0)
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

            foreach (AnimationClip clip in clips)
            {
                EditorGUILayout.LabelField($"{folder}/{GetOutputName(entry, clip, clips.Count > 1)}.fbx", EditorStyles.miniLabel);
            }
        }

        private void DrawExecuteBar()
        {
            bool isValid = FbxExporterBridge.IsAvailable
                && outputDirectoryProp.objectReferenceValue != null
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
            entryProp.FindPropertyRelative("exportAscii").boolValue = false;
            entryProp.FindPropertyRelative("exportContent").enumValueIndex = (int)BakeExportContent.ModelAndAnimation;
            entryProp.FindPropertyRelative("importAnimationType").enumValueIndex = (int)BakedFbxAnimationType.Generic;

            SerializedProperty clipsProp = entryProp.FindPropertyRelative("clips");
            clipsProp.ClearArray();
            if (clips != null)
            {
                foreach (AnimationClip clip in clips)
                {
                    int clipIndex = clipsProp.arraySize;
                    clipsProp.InsertArrayElementAtIndex(clipIndex);
                    clipsProp.GetArrayElementAtIndex(clipIndex).objectReferenceValue = clip;
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

            foreach (Object selected in Selection.objects)
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

        private static string GetEntryLabel(SerializedProperty entryProp)
        {
            string displayName = entryProp.FindPropertyRelative("displayName").stringValue;
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                return displayName.Trim();
            }

            Object fbx = entryProp.FindPropertyRelative("sourceFbx").objectReferenceValue;
            if (fbx != null)
            {
                int clipCount = entryProp.FindPropertyRelative("clips").arraySize;
                return clipCount > 1 ? $"{fbx.name} ({clipCount})" : fbx.name;
            }

            return "(no FBX)";
        }
    }
}
