using System;
using UnityEditor;
using UnityEngine;

namespace YozoLab.PoseBaker
{
    /// <summary>
    /// 再生中のアバターのポーズ（PhysBoneなどの揺れ物が動いた後のTransform）をキャプチャして
    /// AnimationClipとして書き出し、任意のAnimationClipをEditモードのアバターに焼き込むウィンドウ。
    /// </summary>
    public class PlayModePoseBakerWindow : EditorWindow
    {
        private const string PrefScope = "YozoLab.PoseBaker.Scope";
        private const string PrefApplyPosition = "YozoLab.PoseBaker.ApplyPosition";
        private const string PrefApplyRotation = "YozoLab.PoseBaker.ApplyRotation";
        private const string PrefApplyScale = "YozoLab.PoseBaker.ApplyScale";

        [SerializeField] private GameObject avatarRoot;
        [SerializeField] private AnimationClip clipToApply;
        [SerializeField] private float sampleTime;

        private static CaptureData cachedData;
        private static DateTime? cachedTimestamp;

        private string lastMessage;
        private MessageType lastMessageType = MessageType.Info;
        private Vector2 scrollPosition;

        [MenuItem("YozoLab/PlayMode Pose Baker")]
        public static void ShowWindow()
        {
            var window = GetWindow<PlayModePoseBakerWindow>("PlayMode Pose Baker");
            window.minSize = new Vector2(340, 360);
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // 再生開始/終了でGUIの表示内容が切り替わるため再描画
            Repaint();
        }

        private static CaptureData GetCachedCapture()
        {
            DateTime? timestamp = PlayModePoseBaker.GetStorageTimestamp();
            if (timestamp != cachedTimestamp)
            {
                cachedData = PlayModePoseBaker.Load();
                cachedTimestamp = timestamp;
            }
            return cachedData;
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.Space();
            avatarRoot = (GameObject)EditorGUILayout.ObjectField(
                "アバタールート", avatarRoot, typeof(GameObject), true);

            EditorGUILayout.Space();

            if (EditorApplication.isPlaying)
            {
                DrawCaptureSection();
            }
            else
            {
                EditorGUILayout.LabelField("適用チャンネル", EditorStyles.boldLabel);
                (bool position, bool rotation, bool scale) = DrawChannelToggles();

                EditorGUILayout.Space(12);
                DrawExportSection(position, rotation, scale);
                EditorGUILayout.Space(12);
                DrawBakeSection(position, rotation, scale);
            }

            if (!string.IsNullOrEmpty(lastMessage))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(lastMessage, lastMessageType);
            }

            EditorGUILayout.EndScrollView();
        }

        // ---------------------------------------------------------------
        // 再生中: キャプチャ
        // ---------------------------------------------------------------

        private void DrawCaptureSection()
        {
            EditorGUILayout.LabelField("キャプチャ（再生中）", EditorStyles.boldLabel);

            var scope = (CaptureScope)EditorPrefs.GetInt(PrefScope, (int)CaptureScope.PhysBonesOnly);
            scope = (CaptureScope)EditorGUILayout.Popup(
                new GUIContent("キャプチャ対象"),
                (int)scope,
                new[]
                {
                    new GUIContent("全Transform（アニメーションのポーズも含む）"),
                    new GUIContent("PhysBoneの影響ボーンのみ"),
                });
            EditorPrefs.SetInt(PrefScope, (int)scope);

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(avatarRoot == null))
            {
                if (GUILayout.Button("現在のポーズをキャプチャ", GUILayout.Height(30)))
                {
                    CaptureData data = PlayModePoseBaker.Capture(avatarRoot, scope);
                    if (data.transforms.Length == 0)
                    {
                        SetMessage(scope == CaptureScope.PhysBonesOnly
                            ? "PhysBoneの影響ボーンが見つかりませんでした。"
                            : "キャプチャ対象のTransformが見つかりませんでした。", MessageType.Warning);
                    }
                    else
                    {
                        PlayModePoseBaker.Save(data);
                        string registryInfo = data.originalPathCount > 0
                            ? $"NDMF ObjectRegistryからビルド前パスを取得: {data.originalPathCount}/{data.transforms.Length}個\n"
                            : "NDMF ObjectRegistry情報なし（階層パスのみで照合します）\n";
                        SetMessage($"{data.transforms.Length}個のTransformをキャプチャしました。\n" +
                                   registryInfo +
                                   "再生を終了するとAnimationClipとして書き出せます。", MessageType.Info);
                    }
                }
            }

            if (avatarRoot == null)
            {
                EditorGUILayout.HelpBox("アバタールートを指定してください。", MessageType.Info);
            }
        }

        // ---------------------------------------------------------------
        // Editモード: キャプチャ結果のAnimationClip書き出し
        // ---------------------------------------------------------------

        private void DrawExportSection(bool position, bool rotation, bool scale)
        {
            EditorGUILayout.LabelField("書き出し（キャプチャ → AnimationClip）", EditorStyles.boldLabel);

            CaptureData data = GetCachedCapture();
            if (data == null)
            {
                EditorGUILayout.HelpBox(
                    "キャプチャデータがありません。\n" +
                    "シーンを再生し、PhysBoneが安定した状態でこのウィンドウからキャプチャしてください。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("対象アバター", data.avatarRootName);
            EditorGUILayout.LabelField("キャプチャ日時", data.capturedAt);
            EditorGUILayout.LabelField("Transform数", data.transforms.Length.ToString());
            EditorGUILayout.LabelField("ObjectRegistry情報",
                data.originalPathCount > 0 ? $"あり（{data.originalPathCount}個）" : "なし（階層パスのみで照合）");

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(!position && !rotation && !scale))
            {
                if (GUILayout.Button(new GUIContent("AnimationClipとして書き出し...",
                        "キャプチャしたポーズを1キーのAnimationClipアセットとして保存します。" +
                        "上の位置/回転/スケールのチェックが書き出し対象になります。")))
                {
                    ExportAnimationClip(data, position, rotation, scale);
                }
            }
        }

        private void ExportAnimationClip(CaptureData data, bool position, bool rotation, bool scale)
        {
            string assetPath = EditorUtility.SaveFilePanelInProject(
                "AnimationClipとして書き出し",
                $"{data.avatarRootName}_generic",
                "anim",
                "キャプチャしたポーズの保存先を選択してください。");
            if (string.IsNullOrEmpty(assetPath)) return;

            // 生成されるクリップ名は必ず "_generic" で終わるようにする
            assetPath = EnsureGenericSuffix(assetPath);

            AnimationClip clip = PlayModePoseBaker.CreateAnimationClip(
                data, position, rotation, scale,
                out int skippedDuplicateCount, out int skippedHumanoidCount);
            AnimationClip saved = PlayModePoseBaker.SaveAnimationClip(clip, assetPath);
            EditorGUIUtility.PingObject(saved);

            // 続けて焼き込めるよう、書き出したクリップを焼き込み欄にセットする
            clipToApply = saved;

            string message = $"AnimationClipを書き出しました: {assetPath}";
            if (skippedHumanoidCount > 0)
            {
                message += $"\nHumanoidボーンのため除外: {skippedHumanoidCount}個";
            }
            if (skippedDuplicateCount > 0)
            {
                message += $"\n同名兄弟のため書き出せなかったTransform: {skippedDuplicateCount}個" +
                           "（アニメーションのパスでは同名の兄弟を区別できません）";
            }
            SetMessage(message, skippedDuplicateCount > 0 ? MessageType.Warning : MessageType.Info);
        }

        /// <summary>保存先のファイル名（拡張子を除く）が "_generic" で終わるように補正する</summary>
        private static string EnsureGenericSuffix(string assetPath)
        {
            const string suffix = "_generic";
            string dir = System.IO.Path.GetDirectoryName(assetPath);
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            string ext = System.IO.Path.GetExtension(assetPath);

            if (!name.EndsWith(suffix, StringComparison.Ordinal))
            {
                name += suffix;
            }
            if (string.IsNullOrEmpty(dir)) return name + ext;
            // Unityのアセットパスはスラッシュ区切りに統一する
            return $"{dir.Replace('\\', '/')}/{name}{ext}";
        }

        // ---------------------------------------------------------------
        // Editモード: AnimationClipのシーンへの焼き込み
        // ---------------------------------------------------------------

        private void DrawBakeSection(bool position, bool rotation, bool scale)
        {
            EditorGUILayout.LabelField("焼き込み（AnimationClip → シーン）", EditorStyles.boldLabel);

            clipToApply = (AnimationClip)EditorGUILayout.ObjectField(
                new GUIContent("AnimationClip",
                    "書き出したポーズのクリップに限らず、任意のAnimationClipを焼き込めます。" +
                    "Humanoid/Genericのどちらのクリップも扱えます。"),
                clipToApply, typeof(AnimationClip), false);

            if (clipToApply != null && clipToApply.length > 0f)
            {
                sampleTime = EditorGUILayout.Slider(
                    new GUIContent("サンプル時刻（秒）", "クリップ内のどの時刻のポーズを焼き込むか"),
                    sampleTime, 0f, clipToApply.length);
            }

            EditorGUILayout.Space();

            GameObject target = ResolveBakeTarget();

            if (clipToApply != null && clipToApply.isHumanMotion && target != null)
            {
                Animator animator = target.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                {
                    EditorGUILayout.HelpBox(
                        "Humanoidクリップですが、アバタールートのAnimatorにHumanoidの" +
                        "Avatarが設定されていません。マッスルカーブを解決できないため、" +
                        "ボーンに反映されない可能性があります。", MessageType.Warning);
                }
            }

            bool canBake = target != null && clipToApply != null && (position || rotation || scale);
            using (new EditorGUI.DisabledScope(!canBake))
            {
                if (GUILayout.Button("AnimationClipをシーンへ焼き込み", GUILayout.Height(30)))
                {
                    float time = Mathf.Clamp(sampleTime, 0f, clipToApply.length);
                    ApplyResult result = PlayModePoseBaker.ApplyAnimationClip(
                        target, clipToApply, time, position, rotation, scale);
                    SetMessage(
                        $"焼き込みました（Ctrl+Zで戻せます）。\n" +
                        $"適用: {result.appliedCount}個 / 変更なし: {result.unchangedCount}個",
                        MessageType.Info);
                }
            }

            if (target == null)
            {
                EditorGUILayout.HelpBox("アバタールートを指定してください。", MessageType.Info);
            }
        }

        /// <summary>焼き込み先: 指定されたアバタールート、なければ最後のキャプチャ時のパスから探す</summary>
        private GameObject ResolveBakeTarget()
        {
            if (avatarRoot != null) return avatarRoot;
            CaptureData data = GetCachedCapture();
            return data != null ? PlayModePoseBaker.FindByScenePath(data.avatarScenePath) : null;
        }

        private (bool position, bool rotation, bool scale) DrawChannelToggles()
        {
            bool position = EditorGUILayout.ToggleLeft("位置を適用", EditorPrefs.GetBool(PrefApplyPosition, true));
            bool rotation = EditorGUILayout.ToggleLeft("回転を適用", EditorPrefs.GetBool(PrefApplyRotation, true));
            bool scale = EditorGUILayout.ToggleLeft("スケールを適用", EditorPrefs.GetBool(PrefApplyScale, false));
            EditorPrefs.SetBool(PrefApplyPosition, position);
            EditorPrefs.SetBool(PrefApplyRotation, rotation);
            EditorPrefs.SetBool(PrefApplyScale, scale);
            return (position, rotation, scale);
        }

        private void SetMessage(string message, MessageType type)
        {
            lastMessage = message;
            lastMessageType = type;
            Repaint();
        }
    }
}
