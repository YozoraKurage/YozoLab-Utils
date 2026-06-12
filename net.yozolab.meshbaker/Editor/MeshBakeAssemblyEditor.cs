using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YozoLab.MeshBaker
{
    /// <summary>
    /// MeshBakeAssemblyのインスペクタ。
    /// 使用テクスチャの一覧表示と、メインテクスチャ解像度の非破壊な上書き、ベイクの実行を提供する。
    /// </summary>
    [CustomEditor(typeof(MeshBakeAssembly))]
    public class MeshBakeAssemblyEditor : Editor
    {
        /// <summary>1つのメインテクスチャと、それを参照しているマテリアル群</summary>
        private class TextureEntry
        {
            public Texture texture;
            public int originalLongestEdge;
            public readonly List<Material> materials = new List<Material>();
        }

        private static readonly int[] ResolutionOptions = { 0, 2048, 1024, 512, 256, 128, 64, 32 };

        private List<TextureEntry> textureEntries;
        private List<Material> materialsWithoutTexture;

        public override void OnInspectorGUI()
        {
            var assembly = (MeshBakeAssembly)target;

            // rendererGroups だけはドラッグ＆ドロップ対応の独自UIで描画し、残りは既定描画
            serializedObject.Update();
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.propertyPath == "m_Script") continue;
                if (iterator.propertyPath == "rendererGroups")
                {
                    DrawRendererGroups(iterator);
                    continue;
                }
                // Autodesk Interactiveモードでは収集プロパティは自動なので手動指定欄は隠す
                if (iterator.propertyPath == "textureProperties" &&
                    assembly.materialMode == MaterialMode.AutodeskInteractive)
                {
                    EditorGUILayout.HelpBox(
                        "Autodesk Interactiveモードでは収集テクスチャを自動決定します:\n" +
                        string.Join(", ", MaterialModeProfiles.AutodeskInteractiveProperties),
                        MessageType.Info);
                    continue;
                }
                EditorGUILayout.PropertyField(iterator, true);
            }
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("使用テクスチャ / 解像度設定", EditorStyles.boldLabel);

            if (GUILayout.Button("テクスチャを検査"))
            {
                InspectTextures(assembly);
            }

            if (textureEntries != null)
            {
                if (textureEntries.Count == 0 && (materialsWithoutTexture == null || materialsWithoutTexture.Count == 0))
                {
                    EditorGUILayout.HelpBox("検査対象のマテリアルがありません。", MessageType.Info);
                }

                foreach (TextureEntry entry in textureEntries)
                {
                    DrawTextureEntry(assembly, entry);
                }

                if (materialsWithoutTexture != null && materialsWithoutTexture.Count > 0)
                {
                    DrawNoTextureGroup(assembly);
                }
            }

            EditorGUILayout.Space(12);

            bool hasRenderers = assembly.rendererGroups != null &&
                                assembly.rendererGroups.Any(g => g != null && g.renderers != null &&
                                    g.renderers.Any(r => r != null && StaticMeshBaker.GetSharedMesh(r) != null));
            using (new EditorGUI.DisabledScope(!hasRenderers))
            {
                if (GUILayout.Button("静的メッシュにベイク", GUILayout.Height(32)))
                {
                    RunBake(assembly);
                }
            }
            if (!hasRenderers)
            {
                EditorGUILayout.HelpBox("Renderer Groupsにベイク対象のRenderer（SkinnedMeshRenderer/MeshRenderer）を設定してください。", MessageType.Info);
            }
        }

        // ---------------------------------------------------------------
        // Renderer Groups（ドラッグ＆ドロップで子のRendererを再帰追加）
        // ---------------------------------------------------------------

        private void DrawRendererGroups(SerializedProperty groupsProp)
        {
            EditorGUILayout.LabelField("Renderer Groups", EditorStyles.boldLabel);

            int removeGroupIndex = -1;
            for (int g = 0; g < groupsProp.arraySize; g++)
            {
                SerializedProperty groupProp = groupsProp.GetArrayElementAtIndex(g);
                SerializedProperty nameProp = groupProp.FindPropertyRelative("name");
                SerializedProperty renderersProp = groupProp.FindPropertyRelative("renderers");

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        string label = string.IsNullOrEmpty(nameProp.stringValue)
                            ? $"グループ {g + 1}" : nameProp.stringValue;
                        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                        if (GUILayout.Button("グループ削除", GUILayout.Width(90)))
                        {
                            removeGroupIndex = g;
                        }
                    }

                    EditorGUILayout.PropertyField(nameProp, new GUIContent("グループ名"));

                    // 既存のRenderer要素
                    int removeIndex = -1;
                    for (int i = 0; i < renderersProp.arraySize; i++)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.PropertyField(
                                renderersProp.GetArrayElementAtIndex(i), GUIContent.none);
                            if (GUILayout.Button("×", GUILayout.Width(22)))
                            {
                                removeIndex = i;
                            }
                        }
                    }
                    if (removeIndex >= 0) renderersProp.DeleteArrayElementAtIndex(removeIndex);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("空のスロットを追加"))
                        {
                            renderersProp.arraySize++;
                            renderersProp.GetArrayElementAtIndex(renderersProp.arraySize - 1)
                                .objectReferenceValue = null;
                        }
                        if (GUILayout.Button("クリア"))
                        {
                            renderersProp.ClearArray();
                        }
                    }

                    DrawDropArea(renderersProp);
                }
            }

            if (removeGroupIndex >= 0) groupsProp.DeleteArrayElementAtIndex(removeGroupIndex);

            if (GUILayout.Button("＋ グループを追加"))
            {
                groupsProp.arraySize++;
                // 追加要素の中身を初期化（前要素のコピーになるのを防ぐ）
                SerializedProperty added = groupsProp.GetArrayElementAtIndex(groupsProp.arraySize - 1);
                added.FindPropertyRelative("name").stringValue = "";
                added.FindPropertyRelative("renderers").ClearArray();
            }
        }

        /// <summary>
        /// GameObjectをドラッグ＆ドロップすると、その配下のRendererを再帰的に収集して追加するドロップ領域。
        /// </summary>
        private void DrawDropArea(SerializedProperty renderersProp)
        {
            Rect dropRect = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect,
                "ここにオブジェクトをドラッグ＆ドロップ\n（配下のRendererを再帰的に追加）",
                EditorStyles.helpBox);

            Event evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;
            if (!dropRect.Contains(evt.mousePosition)) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type != EventType.DragPerform) return;

            DragAndDrop.AcceptDrag();

            var collected = new List<Renderer>();
            foreach (UnityEngine.Object obj in DragAndDrop.objectReferences)
            {
                GameObject go = obj as GameObject;
                if (go == null && obj is Component component) go = component.gameObject;
                if (go != null) CollectRenderers(go, collected);
            }

            // 既存の参照を集めて重複追加を防ぐ
            var existing = new HashSet<UnityEngine.Object>();
            for (int i = 0; i < renderersProp.arraySize; i++)
            {
                UnityEngine.Object o = renderersProp.GetArrayElementAtIndex(i).objectReferenceValue;
                if (o != null) existing.Add(o);
            }

            foreach (Renderer renderer in collected)
            {
                if (!existing.Add(renderer)) continue;
                renderersProp.arraySize++;
                renderersProp.GetArrayElementAtIndex(renderersProp.arraySize - 1).objectReferenceValue = renderer;
            }

            evt.Use();
        }

        /// <summary>GameObject配下のベイク可能なRendererを再帰的に収集する</summary>
        private static void CollectRenderers(GameObject root, List<Renderer> into)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer)) continue;
                if (StaticMeshBaker.GetSharedMesh(renderer) == null) continue;
                if (!into.Contains(renderer)) into.Add(renderer);
            }
        }

        // ---------------------------------------------------------------
        // テクスチャ1件の描画（左: サムネイル / 右: 解像度設定 + 参照マテリアル）
        // ---------------------------------------------------------------

        private void DrawTextureEntry(MeshBakeAssembly assembly, TextureEntry entry)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            using (new EditorGUILayout.HorizontalScope())
            {
                // 左: サムネイル
                Rect thumbRect = GUILayoutUtility.GetRect(64, 64, GUILayout.Width(64), GUILayout.Height(64));
                EditorGUI.DrawPreviewTexture(thumbRect, entry.texture);

                // 右: テクスチャ名・解像度設定・参照マテリアル
                using (new EditorGUILayout.VerticalScope())
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.ObjectField(entry.texture, typeof(Texture), false);
                    }
                    EditorGUILayout.LabelField("元解像度", $"{entry.texture.width} x {entry.texture.height}");
                    DrawResolutionPopup(assembly, entry);

                    EditorGUILayout.LabelField($"参照マテリアル ({entry.materials.Count})", EditorStyles.miniBoldLabel);
                    foreach (Material material in entry.materials)
                    {
                        EditorGUILayout.LabelField("    • " + material.name, EditorStyles.miniLabel);
                    }
                }
            }
        }

        private void DrawNoTextureGroup(MeshBakeAssembly assembly)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    $"メインテクスチャなし ({materialsWithoutTexture.Count}マテリアル)", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField("（マテリアル色で代替されます。解像度設定は不要です）", EditorStyles.miniLabel);
                foreach (Material material in materialsWithoutTexture)
                {
                    EditorGUILayout.LabelField("    • " + material.name, EditorStyles.miniLabel);
                }
            }
        }

        private void DrawResolutionPopup(MeshBakeAssembly assembly, TextureEntry entry)
        {
            int current = assembly.GetTargetResolution(entry.texture);

            var labels = new string[ResolutionOptions.Length];
            for (int i = 0; i < ResolutionOptions.Length; i++)
            {
                int value = ResolutionOptions[i];
                if (value == 0)
                {
                    labels[i] = "原寸（上書きなし）";
                }
                else
                {
                    float optionScale = Mathf.Clamp(value / (float)Mathf.Max(entry.originalLongestEdge, 1), 0.01f, 1f);
                    labels[i] = value > entry.originalLongestEdge
                        ? $"{value}px（原寸以上 → 原寸）"
                        : $"{value}px（約{optionScale:P0}）";
                }
            }

            int currentIndex = Array.IndexOf(ResolutionOptions, current);
            string[] displayLabels = labels;
            int[] values = ResolutionOptions;
            if (currentIndex < 0)
            {
                // 既存設定がプリセット外のカスタム値の場合は先頭に追加して表示する
                displayLabels = new[] { $"{current}px（カスタム）" }.Concat(labels).ToArray();
                values = new[] { current }.Concat(ResolutionOptions).ToArray();
                currentIndex = 0;
            }

            int newIndex = EditorGUILayout.Popup("目標解像度", currentIndex, displayLabels);
            int newValue = values[newIndex];
            if (newValue != current)
            {
                SetTargetResolution(assembly, entry.texture, newValue);
            }
        }

        /// <summary>解像度上書きをコンポーネントに非破壊で書き込む（Undo対応）</summary>
        private static void SetTargetResolution(MeshBakeAssembly assembly, Texture texture, int value)
        {
            Undo.RecordObject(assembly, "Set Texture Resolution");

            TextureResolutionSetting setting =
                assembly.textureResolutionSettings.FirstOrDefault(s => s != null && s.texture == texture);

            if (value == 0)
            {
                // 原寸（上書きなし）はエントリ自体を削除して設定を残さない
                if (setting != null) assembly.textureResolutionSettings.Remove(setting);
            }
            else if (setting != null)
            {
                setting.targetResolution = value;
            }
            else
            {
                assembly.textureResolutionSettings.Add(new TextureResolutionSetting
                {
                    texture = texture,
                    targetResolution = value,
                });
            }

            EditorUtility.SetDirty(assembly);
        }

        // ---------------------------------------------------------------
        // ベイク実行
        // ---------------------------------------------------------------

        private void RunBake(MeshBakeAssembly assembly)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Mesh Baker", "ベイク中...", 0.5f);
                BakeReport report = StaticMeshBaker.Bake(assembly);
                EditorUtility.ClearProgressBar();

                string summary =
                    $"ベイクが完了しました。\n\n" +
                    $"総頂点数: {report.vertexCount}\n" +
                    $"元マテリアル数: {report.sourceMaterialCount} → 総サブメッシュ数: {report.submeshCount}\n" +
                    $"Mesh ({report.meshPaths.Count}グループ):\n- " + string.Join("\n- ", report.meshPaths) +
                    (report.materialPath != null ? $"\nMaterial: {report.materialPath}" : "") +
                    (report.prefabPaths.Count > 0 ? $"\nPrefab:\n- " + string.Join("\n- ", report.prefabPaths) : "");
                if (report.infos.Count > 0)
                {
                    summary += "\n\n最適化:\n- " + string.Join("\n- ", report.infos);
                }
                if (report.warnings.Count > 0)
                {
                    summary += "\n\n警告:\n- " + string.Join("\n- ", report.warnings);
                    Debug.LogWarning("[MeshBaker] " + string.Join("\n", report.warnings));
                }
                EditorUtility.DisplayDialog("Mesh Baker", summary, "OK");

                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(report.meshPaths[0]);
                if (mesh != null) EditorGUIUtility.PingObject(mesh);
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Mesh Baker", $"ベイクに失敗しました:\n{e.Message}", "OK");
                Debug.LogException(e);
            }
        }

        // ---------------------------------------------------------------
        // テクスチャ検査（同一参照テクスチャをまとめる）
        // ---------------------------------------------------------------

        private void InspectTextures(MeshBakeAssembly assembly)
        {
            textureEntries = new List<TextureEntry>();
            materialsWithoutTexture = new List<Material>();
            // マテリアルモードに応じた収集プロパティの先頭をメインとして検査する
            string mainProperty = MaterialModeProfiles.GetCollectProperties(
                assembly.materialMode, assembly.textureProperties)[0];

            // 使用中のユニークなマテリアルを収集
            var materials = new List<Material>();
            IEnumerable<Renderer> allRenderers = assembly.rendererGroups
                .Where(g => g != null && g.renderers != null)
                .SelectMany(g => g.renderers);

            foreach (Renderer renderer in allRenderers
                         .Where(r => r != null && StaticMeshBaker.GetSharedMesh(r) != null))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null && !materials.Contains(material)) materials.Add(material);
                }
            }

            // メインテクスチャ単位でまとめる（同一テクスチャを参照する複数マテリアルを1エントリに集約）
            var byTexture = new Dictionary<Texture, TextureEntry>();
            foreach (Material material in materials)
            {
                Texture mainTexture = material.HasProperty(mainProperty)
                    ? material.GetTexture(mainProperty) : null;
                if (mainTexture == null)
                {
                    materialsWithoutTexture.Add(material);
                    continue;
                }

                if (!byTexture.TryGetValue(mainTexture, out TextureEntry entry))
                {
                    entry = new TextureEntry
                    {
                        texture = mainTexture,
                        originalLongestEdge = Mathf.Max(mainTexture.width, mainTexture.height),
                    };
                    byTexture.Add(mainTexture, entry);
                    textureEntries.Add(entry);
                }
                entry.materials.Add(material);
            }
        }
    }
}
