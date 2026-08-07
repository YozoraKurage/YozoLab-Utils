using System.Text;
using UnityEditor;
using UnityEngine;

namespace YozoLab.MeshBaker
{
    /// <summary>
    /// 再生中のアバターの現在状態（揺れ物が落ち着いたポーズ・ブレンドシェイプ・表示状態）を、
    /// 再生中のまま静的メッシュ化（NDMFビルド後の状態そのまま）するウィンドウ。
    /// 必要なら中間生成物として「固定Prefab」も保存できる。
    /// </summary>
    public class FrozenAvatarBakerWindow : EditorWindow
    {
        private const string PrefEmbedMode = "YozoLab.MeshBaker.Frozen.EmbedMode";
        private const string PrefOutputDir = "YozoLab.MeshBaker.Frozen.OutputDir";

        [SerializeField] private GameObject avatarRoot;

        private string lastMessage;
        private MessageType lastMessageType = MessageType.Info;
        private Vector2 scrollPosition;

        [MenuItem("YozoLab/Frozen Avatar Baker")]
        public static void ShowWindow()
        {
            var window = GetWindow<FrozenAvatarBakerWindow>("Frozen Avatar Baker");
            window.minSize = new Vector2(360, 360);
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            if (avatarRoot == null) avatarRoot = Selection.activeGameObject;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // 再生開始/終了でボタンの有効/無効が切り替わるため再描画
            Repaint();
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "再生中のアバターの現在の状態（揺れ物が落ち着いたポーズ・ブレンドシェイプ・表示状態）を、" +
                "再生中のまま静的メッシュ化します。\n" +
                "再生中（NDMFビルド後）のアバターを対象にするため、editorに戻ってベイクする場合と違い" +
                "「ビルド前」ではなく見えている状態そのものがベイクされます。",
                MessageType.None);

            EditorGUILayout.Space();
            avatarRoot = (GameObject)EditorGUILayout.ObjectField(
                "アバタールート", avatarRoot, typeof(GameObject), true);

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("再生中に実行してください。", MessageType.Info);
            }
            else if (avatarRoot == null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("アバタールートを指定してください。", MessageType.Info);
            }

            EditorGUILayout.Space();
            DrawStaticBakeSection();

            EditorGUILayout.Space(12);
            DrawFreezePrefabSection();

            if (!string.IsNullOrEmpty(lastMessage))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(lastMessage, lastMessageType);
            }

            EditorGUILayout.EndScrollView();
        }

        // ---------------------------------------------------------------
        // 一気に静的メッシュ化（推奨）
        // ---------------------------------------------------------------

        private void DrawStaticBakeSection()
        {
            EditorGUILayout.LabelField("静的メッシュ化（再生中）", EditorStyles.boldLabel);

            string outputDir = EditorPrefs.GetString(PrefOutputDir, "Assets/BakedMeshes");
            outputDir = EditorGUILayout.TextField(
                new GUIContent("出力先フォルダ",
                    "成果物（Mesh/Texture/Material/Prefab）の出力先。MeshBakeGroup未設定時の自動ベイクや、" +
                    "設定済みでもこのフォルダが使われます。"),
                outputDir);
            EditorPrefs.SetString(PrefOutputDir, outputDir);

            bool hasGroup = avatarRoot != null && avatarRoot.GetComponentInChildren<MeshBakeGroup>(true) != null;
            if (avatarRoot != null)
            {
                EditorGUILayout.HelpBox(
                    hasGroup
                        ? "アバターのMeshBakeGroup設定（グループ分け・マテリアル統合など）を使ってベイクします。"
                        : "MeshBakeGroupが見つからないため、全Rendererを自動でグループ分けしてベイクします。",
                    MessageType.None);
            }

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || avatarRoot == null))
            {
                if (GUILayout.Button("再生中の状態を静的メッシュ化", GUILayout.Height(32)))
                {
                    RunStaticBake(outputDir);
                }
            }
        }

        private void RunStaticBake(string outputDir)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Frozen Avatar Baker", "静的メッシュ化中...", 0.5f);
                FrozenAvatarBaker.StaticBakeResult result =
                    FrozenAvatarBaker.BakeStatic(avatarRoot, string.IsNullOrEmpty(outputDir) ? null : outputDir.Trim());
                EditorUtility.ClearProgressBar();

                BakeReport report = result.report;
                var sb = new StringBuilder();
                sb.AppendLine("静的メッシュ化が完了しました。");
                sb.AppendLine(result.usedExistingGroup
                    ? $"MeshBakeGroupの設定を使用（{result.groupCount}グループ）"
                    : $"自動グループ分け（{result.groupCount}グループ）");
                sb.AppendLine($"総頂点数: {report.vertexCount} / サブメッシュ: {report.submeshCount}");
                foreach (string path in report.meshPaths) sb.AppendLine($"Mesh: {path}");
                if (report.materialPath != null) sb.AppendLine($"Material: {report.materialPath}");
                foreach (string path in report.prefabPaths) sb.AppendLine($"Prefab: {path}");
                foreach (string info in report.infos) sb.AppendLine($"・{info}");
                foreach (string warning in report.warnings) sb.AppendLine($"⚠ {warning}");

                MessageType type = report.warnings.Count > 0 ? MessageType.Warning : MessageType.Info;
                SetMessage(sb.ToString().TrimEnd(), type);

                GameObject prefab = report.prefabPaths.Count > 0
                    ? AssetDatabase.LoadAssetAtPath<GameObject>(report.prefabPaths[0]) : null;
                Object pingTarget = prefab != null
                    ? (Object)prefab
                    : (report.meshPaths.Count > 0 ? AssetDatabase.LoadAssetAtPath<Mesh>(report.meshPaths[0]) : null);
                if (pingTarget != null)
                {
                    EditorGUIUtility.PingObject(pingTarget);
                    Selection.activeObject = pingTarget;
                }
                if (report.warnings.Count > 0)
                    Debug.LogWarning("[FrozenAvatarBaker] " + string.Join("\n", report.warnings));
            }
            catch (System.Exception e)
            {
                EditorUtility.ClearProgressBar();
                SetMessage($"静的メッシュ化に失敗しました: {e.Message}", MessageType.Error);
                Debug.LogError($"[FrozenAvatarBaker] {e}");
            }
        }

        // ---------------------------------------------------------------
        // 固定Prefabの保存（任意・ベイク前段の中間生成物）
        // ---------------------------------------------------------------

        private void DrawFreezePrefabSection()
        {
            EditorGUILayout.LabelField("固定Prefabとして保存（任意）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "静的メッシュ化せず、固定状態のアバターを自己完結Prefabとして保存します。" +
                "後で個別にMesh Bake Groupで調整したい場合に使います。",
                MessageType.None);

            var embedMode = (FrozenAvatarBaker.AssetEmbedMode)EditorPrefs.GetInt(
                PrefEmbedMode, (int)FrozenAvatarBaker.AssetEmbedMode.EmbedAsSubAssets);
            embedMode = (FrozenAvatarBaker.AssetEmbedMode)EditorGUILayout.Popup(
                new GUIContent("生成物の埋め込み方式",
                    "再生中に動的生成された（プロジェクト内に存在しない）メッシュ/マテリアル/テクスチャの保存方法。"),
                (int)embedMode,
                new[]
                {
                    new GUIContent("サブアセットとして埋め込み（1ファイル）"),
                    new GUIContent("外部フォルダに書き出し"),
                });
            EditorPrefs.SetInt(PrefEmbedMode, (int)embedMode);

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || avatarRoot == null))
            {
                if (GUILayout.Button("固定Prefabとして保存..."))
                {
                    RunFreezePrefab(embedMode);
                }
            }
        }

        private void RunFreezePrefab(FrozenAvatarBaker.AssetEmbedMode embedMode)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "固定Prefabとして保存",
                $"{avatarRoot.name}_Frozen",
                "prefab",
                "固定したアバターの保存先を選択してください。");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                EditorUtility.DisplayProgressBar("Frozen Avatar Baker", "アバターを固定中...", 0.5f);
                FrozenAvatarBaker.FreezeReport report = FrozenAvatarBaker.Freeze(avatarRoot, path, embedMode);
                EditorUtility.ClearProgressBar();

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(report.prefabPath);
                EditorGUIUtility.PingObject(prefab);
                Selection.activeObject = prefab;

                var sb = new StringBuilder();
                sb.AppendLine($"固定したPrefabを保存しました: {report.prefabPath}");
                sb.AppendLine($"除去したコンポーネント: {report.strippedComponentCount}個");
                sb.AppendLine($"埋め込み メッシュ:{report.embeddedMeshCount} / " +
                              $"マテリアル:{report.embeddedMaterialCount} / テクスチャ:{report.embeddedTextureCount}");
                foreach (string warning in report.warnings) sb.AppendLine($"⚠ {warning}");

                MessageType type = report.warnings.Count > 0 ? MessageType.Warning : MessageType.Info;
                SetMessage(sb.ToString().TrimEnd(), type);
                if (report.warnings.Count > 0)
                {
                    foreach (string warning in report.warnings)
                        Debug.LogWarning($"[FrozenAvatarBaker] {warning}", prefab);
                }
            }
            catch (System.Exception e)
            {
                EditorUtility.ClearProgressBar();
                SetMessage($"固定に失敗しました: {e.Message}", MessageType.Error);
                Debug.LogError($"[FrozenAvatarBaker] {e}");
            }
        }

        private void SetMessage(string message, MessageType type)
        {
            lastMessage = message;
            lastMessageType = type;
            Repaint();
        }
    }
}
