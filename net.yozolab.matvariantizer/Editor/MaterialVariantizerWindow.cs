using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YozoLab.MatVariantizer
{
    /// <summary>
    /// 現在のシーンの VRChat アバターを再帰スキャンし、マテリアル（とテクスチャ）を
    /// アバター別の Variant / コピーへ非破壊にローカライズするエディタウィンドウ。
    /// </summary>
    public class MaterialVariantizerWindow : EditorWindow
    {
        private LocalizationPlan plan;
        private string baseFolder = string.Empty;
        private bool includeTextures;
        private Vector2 scroll;
        private string scanMessage;
        private LocalizationApplier.Report lastReport;
        // plan の構造変更はフレーム途中で行うと IMGUI のレイアウト不整合を招くため、
        // Layout イベントの先頭で再構築する。
        private bool pendingRescan;
        private bool monitorExpanded = true;

        [MenuItem("YozoLab/Material Variantizer")]
        public static void ShowWindow()
        {
            GetWindow<MaterialVariantizerWindow>("Material Variantizer");
        }

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(baseFolder))
                baseFolder = DefaultBaseFolder();
            pendingRescan = true; // ウィンドウを開いた時点で自動スキャンする
        }

        private void OnGUI()
        {
            if (pendingRescan && Event.current.type == EventType.Layout)
            {
                pendingRescan = false;
                BuildPlanFromScene();
            }

            DrawOptions();
            DrawScanControls();
            DrawPlan();
            DrawFooter();
            DrawMonitorSection();
        }

        // ---- オプション ----

        private void DrawOptions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("保存先ベース", GUILayout.Width(80));
                baseFolder = EditorGUILayout.TextField(baseFolder);
                if (GUILayout.Button("シーン位置", GUILayout.Width(80)))
                    baseFolder = DefaultBaseFolder();
            }

            EditorGUI.BeginChangeCheck();
            includeTextures = EditorGUILayout.ToggleLeft(
                "テクスチャもローカライズする", includeTextures);
            if (EditorGUI.EndChangeCheck() && plan != null)
                pendingRescan = true; // テクスチャ行の有無が変わるため再構築
        }

        private void DrawScanControls()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("現在のシーンをスキャン", GUILayout.Height(26)))
                {
                    lastReport = null;
                    pendingRescan = true;
                }

                if (plan != null && GUILayout.Button("全選択", GUILayout.Width(70)))
                    SetAllSelected(true);
                if (plan != null && GUILayout.Button("全解除", GUILayout.Width(70)))
                    SetAllSelected(false);
            }

            if (!string.IsNullOrEmpty(scanMessage))
                EditorGUILayout.HelpBox(scanMessage, MessageType.Warning);
        }

        // ---- プレビュー ----

        private void DrawPlan()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (plan == null)
            {
                EditorGUILayout.LabelField("「現在のシーンをスキャン」で対象を読み込みます。");
            }
            else if (plan.IsEmpty)
            {
                EditorGUILayout.LabelField("対象アバターが見つかりませんでした。");
            }
            else
            {
                foreach (AvatarGroup avatar in plan.avatars)
                    DrawAvatar(avatar);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawAvatar(AvatarGroup avatar)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    bool all = IsAvatarAllSelected(avatar);
                    bool nv = EditorGUILayout.Toggle(all, GUILayout.Width(18));
                    if (EditorGUI.EndChangeCheck())
                    {
                        SetAvatarSelected(avatar, nv);
                        VariantPlanner.NormalizeSelection(plan);
                    }

                    // EditorGUILayout.Foldout は行全体のクリックを奪い隣のチェックボックスが
                    // 反応しなくなるため、マテリアル行と同じくボタン式の foldout にする。
                    string symbol = avatar.expanded ? "▼ " : "▶ ";
                    if (GUILayout.Button($"{symbol}{avatar.name}  ({avatar.materials.Count} materials)",
                            EditorStyles.boldLabel))
                        avatar.expanded = !avatar.expanded;
                }

                if (!avatar.expanded) return;

                EditorGUILayout.LabelField(avatar.folder, EditorStyles.miniLabel);

                foreach (MaterialPlan mp in avatar.materials)
                    DrawMaterial(mp);
            }
        }

        /// <summary>アバター配下の対象可能マテリアルがすべて選択済みか。</summary>
        private static bool IsAvatarAllSelected(AvatarGroup avatar)
        {
            bool any = false;
            foreach (MaterialPlan mp in avatar.materials)
            {
                if (mp.alreadyBound) continue; // 適用済みは判定から除外
                any = true;
                if (!mp.selected) return false;
            }
            return any;
        }

        private void SetAvatarSelected(AvatarGroup avatar, bool value)
        {
            foreach (MaterialPlan mp in avatar.materials)
                mp.selected = value && !mp.alreadyBound;
            if (includeTextures)
                foreach (TexturePlan tp in avatar.textures)
                    tp.selected = value && tp.Actionable;
        }

        private void DrawMaterial(MaterialPlan mp)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                bool sel = EditorGUILayout.Toggle(mp.selected, GUILayout.Width(18));
                if (EditorGUI.EndChangeCheck())
                {
                    mp.selected = sel;
                    VariantPlanner.NormalizeSelection(plan);
                }

                bool hasTextures = includeTextures && mp.textureProps.Count > 0;
                string foldSymbol = hasTextures ? (mp.expanded ? "▼ " : "▶ ") : "   ";
                if (hasTextures)
                {
                    if (GUILayout.Button(foldSymbol + mp.original.name, EditorStyles.label))
                        mp.expanded = !mp.expanded;
                }
                else
                {
                    EditorGUILayout.LabelField(foldSymbol + (mp.original != null ? mp.original.name : "(null)"));
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(MaterialStatusText(mp), EditorStyles.miniLabel, GUILayout.Width(220));
            }

            if (includeTextures && mp.expanded)
            {
                foreach (TextureProp tprop in mp.textureProps)
                    DrawTextureRow(tprop);
            }
        }

        private void DrawTextureRow(TextureProp tprop)
        {
            TexturePlan tp = tprop.texture;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(28);
                using (new EditorGUI.DisabledScope(!tp.Actionable))
                {
                    EditorGUI.BeginChangeCheck();
                    bool sel = EditorGUILayout.Toggle(tp.selected, GUILayout.Width(18));
                    if (EditorGUI.EndChangeCheck())
                    {
                        VariantPlanner.SelectTexture(tp, sel);
                        VariantPlanner.NormalizeSelection(plan);
                    }
                }

                EditorGUILayout.LabelField(
                    $"{tprop.property} : {(tp.originalTexture != null ? tp.originalTexture.name : "(null)")}");
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(TextureStatusText(tp), EditorStyles.miniLabel, GUILayout.Width(160));
            }
        }

        // ---- フッタ / 実行 ----

        private void DrawFooter()
        {
            if (plan == null || plan.IsEmpty) return;

            CountSelection(out int mats, out int texs);
            EditorGUILayout.LabelField(
                includeTextures
                    ? $"選択: マテリアル {mats} / テクスチャ {texs}"
                    : $"選択: マテリアル {mats}");

            using (new EditorGUI.DisabledScope(mats == 0))
            {
                if (GUILayout.Button("Apply（選択をローカライズ）", GUILayout.Height(30)))
                    ApplyPlan();
            }

            if (lastReport != null)
                DrawReport(lastReport);
        }

        private void DrawReport(LocalizationApplier.Report report)
        {
            string summary =
                $"完了: Variant 生成 {report.variantsCreated} / 再利用 {report.variantsReused}, " +
                $"テクスチャ 複製 {report.texturesCreated} / 再利用 {report.texturesReused}, " +
                $"参照差し替え {report.slotsRebound}";
            EditorGUILayout.HelpBox(summary, MessageType.Info);
            foreach (string w in report.warnings)
                EditorGUILayout.HelpBox(w, MessageType.Warning);
        }

        // ---- 動作 ----

        private void BuildPlanFromScene()
        {
            scanMessage = null;

            List<GameObject> roots = AvatarScanner.FindAvatarRoots();
            if (roots.Count == 0)
            {
                if (AvatarScanner.ResolveDescriptorType() == null)
                    scanMessage = "VRChat Avatars SDK が見つかりません。アバターを選択して再スキャンするとフォールバックします。";

                if (Selection.gameObjects.Length > 0)
                {
                    roots = new List<GameObject>(Selection.gameObjects);
                    scanMessage = "VRCAvatarDescriptor が見つからないため、選択中の GameObject をルートとして使用します。";
                }
            }

            if (string.IsNullOrEmpty(baseFolder))
                baseFolder = DefaultBaseFolder();

            plan = VariantPlanner.Build(roots, baseFolder, includeTextures);
        }

        private void ApplyPlan()
        {
            string normalized = baseFolder.Replace('\\', '/').TrimEnd('/');
            if (!normalized.StartsWith("Assets"))
            {
                EditorUtility.DisplayDialog("Material Variantizer",
                    "保存先ベースは Assets/ 配下を指定してください。", "OK");
                return;
            }

            CountSelection(out int mats, out int texs);
            string message = includeTextures
                ? $"マテリアル {mats} 件、テクスチャ {texs} 件をローカライズします。\n原本には手を加えません。続行しますか？"
                : $"マテリアル {mats} 件をローカライズします。\n原本には手を加えません。続行しますか？";
            if (!EditorUtility.DisplayDialog("Material Variantizer", message, "Apply", "キャンセル"))
                return;

            lastReport = LocalizationApplier.Apply(plan, includeTextures);
            pendingRescan = true; // 状態（適用済み等）を反映するため再スキャン
        }

        private void SetAllSelected(bool value)
        {
            foreach (AvatarGroup avatar in plan.avatars)
            {
                foreach (MaterialPlan mp in avatar.materials)
                    mp.selected = value && !mp.alreadyBound;
                if (includeTextures)
                    foreach (TexturePlan tp in avatar.textures)
                        tp.selected = value && tp.Actionable;
            }
            VariantPlanner.NormalizeSelection(plan);
        }

        private void CountSelection(out int mats, out int texs)
        {
            mats = 0;
            texs = 0;
            foreach (AvatarGroup avatar in plan.avatars)
            {
                foreach (MaterialPlan mp in avatar.materials)
                    if (mp.selected) mats++;
                foreach (TexturePlan tp in avatar.textures)
                    if (tp.selected) texs++;
            }
        }

        // ---- テクスチャ同期モニタ ----

        private void DrawMonitorSection()
        {
            MaterialSyncSettings settings = MaterialSyncSettings.instance;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                monitorExpanded = EditorGUILayout.Foldout(
                    monitorExpanded, "テクスチャ同期モニタ（共有参照の追従）", true);
                if (!monitorExpanded) return;

                EditorGUI.BeginChangeCheck();
                bool en = EditorGUILayout.ToggleLeft("監視を有効にする", settings.enabled);
                if (EditorGUI.EndChangeCheck())
                {
                    settings.enabled = en;
                    settings.Persist();
                    MaterialSyncMonitor.InvalidateWatchSet();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(plan == null || plan.IsEmpty))
                    {
                        if (GUILayout.Button("スキャン中のアバターを登録"))
                            RegisterScanned(settings);
                    }
                    using (new EditorGUI.DisabledScope(Selection.gameObjects.Length == 0))
                    {
                        if (GUILayout.Button("選択を登録"))
                            RegisterSelection(settings);
                    }
                }

                if (settings.avatarGlobalIds.Count == 0)
                {
                    EditorGUILayout.LabelField("登録アバターはありません。", EditorStyles.miniLabel);
                    return;
                }

                string toRemove = null;
                foreach (string id in settings.avatarGlobalIds)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GameObject go = AvatarRegistry.Resolve(id);
                        EditorGUILayout.LabelField(go != null ? go.name : "(シーン外 / 未解決)");
                        if (GUILayout.Button("解除", GUILayout.Width(60)))
                            toRemove = id;
                    }
                }
                if (toRemove != null)
                {
                    settings.Remove(toRemove);
                    MaterialSyncMonitor.InvalidateWatchSet();
                    GUIUtility.ExitGUI(); // リスト行数が変わるため GUI を再開
                }
            }
        }

        private void RegisterScanned(MaterialSyncSettings settings)
        {
            foreach (AvatarGroup a in plan.avatars)
                settings.Add(AvatarRegistry.GetId(a.root));
            MaterialSyncMonitor.InvalidateWatchSet();
            GUIUtility.ExitGUI();
        }

        private void RegisterSelection(MaterialSyncSettings settings)
        {
            foreach (GameObject go in Selection.gameObjects)
                settings.Add(AvatarRegistry.GetId(go));
            MaterialSyncMonitor.InvalidateWatchSet();
            GUIUtility.ExitGUI();
        }

        // ---- 表示テキスト ----

        private static string MaterialStatusText(MaterialPlan mp)
        {
            if (mp.alreadyBound) return "適用済み";
            string status = mp.status == MaterialRowStatus.New ? "新規" : "既存再利用";
            if (mp.AnySlotOutsidePrefab) status += " / Prefab外";
            return status;
        }

        private static string TextureStatusText(TexturePlan tp)
        {
            switch (tp.status)
            {
                case TextureRowStatus.New: return "複製";
                case TextureRowStatus.ReuseExisting: return "既存再利用";
                case TextureRowStatus.AlreadyLocalized: return "適用済み";
                case TextureRowStatus.Embedded: return "埋め込み(不可)";
                default: return string.Empty;
            }
        }

        private static string DefaultBaseFolder()
        {
            Scene scene = SceneManager.GetActiveScene();
            string dir = string.IsNullOrEmpty(scene.path)
                ? "Assets"
                : Path.GetDirectoryName(scene.path).Replace('\\', '/');
            return $"{dir}/MaterialVariants";
        }
    }
}
