using UnityEditor;
using UnityEngine;

namespace YozoLab.ParticleTimeScrubber
{
    /// <summary>
    /// ParticleSystem(子を含むエフェクト全体)の再生位置を、Edit モードのまま
    /// 時間バーで自由に行き来しながら確認するためのエディタ拡張。
    ///
    /// パラメータの編集は既存の Inspector で行う前提で、このウィンドウは
    /// 「時間の操作」と「その時刻の状態を Scene ビューへ反映すること」だけを担当する。
    ///
    ///   - ParticleTimeScrubberWindow.cs            … ライフサイクルと GUI
    ///   - ParticleTimeScrubberWindow.Simulation.cs … 対象の確保/解放・シード固定・シミュレート・変更検知
    /// </summary>
    public partial class ParticleTimeScrubberWindow : EditorWindow
    {
        [MenuItem("YozoLab/Particle Time Scrubber")]
        public static void ShowWindow()
        {
            GetWindow<ParticleTimeScrubberWindow>("Particle Time Scrubber");
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseTarget;
            Undo.postprocessModifications += OnPostprocessModifications;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= ReleaseTarget;
            Undo.postprocessModifications -= OnPostprocessModifications;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;

            ReleaseTarget();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            // Play モードに入る前に必ず手を放す。シードの書き換えを残したまま
            // 実行に入ると、ゲーム側の見え方まで固定されてしまう。
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                ReleaseTarget();
                Repaint();
            }
        }

        // ---------------------------------------------------------------
        // GUI
        // ---------------------------------------------------------------

        private void OnGUI()
        {
            DrawLanguageToggle();

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox(
                    L10n.T("Play モード中は使用できません。Edit モードで使用してください。",
                           "Not available in Play mode. Use it in Edit mode."),
                    MessageType.Info);
                return;
            }

            DrawTargetField();

            if (root == null)
            {
                EditorGUILayout.HelpBox(
                    L10n.T("シーン上の ParticleSystem を指定してください。子の ParticleSystem もまとめて扱います。",
                           "Assign a ParticleSystem in the scene. Child ParticleSystems are handled together."),
                    MessageType.Info);
                return;
            }

            if (!root.gameObject.activeInHierarchy)
            {
                EditorGUILayout.HelpBox(
                    L10n.T("対象の GameObject が非アクティブです。アクティブにしないと表示されません。",
                           "The target GameObject is inactive and will not be rendered."),
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            DrawTimeBar();
            EditorGUILayout.Space();
            DrawTransportButtons();
        }

        private void DrawLanguageToggle()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                L10n.IsEnglish = GUILayout.Toggle(L10n.IsEnglish, "EN", EditorStyles.miniButton, GUILayout.Width(36f));
            }
        }

        private void DrawTargetField()
        {
            EditorGUI.BeginChangeCheck();
            var picked = (ParticleSystem)EditorGUILayout.ObjectField(
                L10n.T("対象エフェクト", "Target Effect"), root, typeof(ParticleSystem), true);
            if (EditorGUI.EndChangeCheck())
            {
                AcquireTarget(picked);
            }
        }

        private void DrawTimeBar()
        {
            EditorGUI.BeginChangeCheck();
            float newTime = EditorGUILayout.Slider(
                L10n.T("時刻 (秒)", "Time (s)"), currentTime, 0f, maxTime);
            if (EditorGUI.EndChangeCheck())
            {
                // バーを触ったら再生は止める。掴んだままの微調整を優先する。
                isPlaying = false;
                SetTime(newTime);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                float newMax = EditorGUILayout.FloatField(L10n.T("最大時刻", "Max Time"), maxTime);
                if (EditorGUI.EndChangeCheck() && newMax > 0.01f)
                {
                    maxTime = newMax;
                    if (currentTime > maxTime) SetTime(maxTime);
                }

                if (GUILayout.Button(L10n.T("自動", "Auto"), GUILayout.Width(60f)))
                {
                    maxTime = EstimateDuration(root);
                    if (currentTime > maxTime) SetTime(maxTime);
                }
            }
        }

        private void DrawTransportButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(L10n.T("|◀ 先頭", "|◀ Start"), GUILayout.Height(24f)))
                {
                    isPlaying = false;
                    SetTime(0f);
                }

                string playLabel = isPlaying
                    ? L10n.T("❚❚ 一時停止", "❚❚ Pause")
                    : L10n.T("▶ 再生", "▶ Play");
                if (GUILayout.Button(playLabel, GUILayout.Height(24f)))
                {
                    if (isPlaying)
                    {
                        isPlaying = false;
                        // 停止時に決定論的な絵へ揃え直す。再生中の逐次シミュレートは
                        // フレーム間隔に依存するので、止まった瞬間の絵を
                        // 「その時刻をバーで指定したときと同じ絵」に保証する。
                        SetTime(currentTime);
                    }
                    else
                    {
                        if (currentTime >= maxTime) SetTime(0f);
                        lastEditorTime = EditorApplication.timeSinceStartup;
                        isPlaying = true;
                    }
                }

                loopPlayback = GUILayout.Toggle(
                    loopPlayback, L10n.T("ループ", "Loop"), EditorStyles.miniButton,
                    GUILayout.Width(60f), GUILayout.Height(24f));
            }
        }

        protected static class L10n
        {
            private const string PrefKey = "YozoLab_ParticleTimeScrubber_Language";

            public static bool IsEnglish
            {
                get => EditorPrefs.GetBool(PrefKey, false);
                set => EditorPrefs.SetBool(PrefKey, value);
            }

            public static string T(string jp, string en) => IsEnglish ? en : jp;
        }
    }
}
