using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace YozoLab.ParticleTimeScrubber
{
    /// <summary>
    /// Scene ビューに出す操作パネル。Unity 標準の Particle Effect パネルと同じく、
    /// ParticleSystem(またはその子)を選択すると現れ、選択を外すと消える。
    ///
    ///   - ParticleTimeScrubberOverlay.cs        … オーバーレイ本体と時間バー・再生操作
    ///   - ParticleTimeScrubberOverlay.Colors.cs … 「色変更」パネル
    ///
    /// シミュレートや対象管理の実体は ParticleScrubController が持つ。
    /// </summary>
    [Overlay(typeof(SceneView), "ParticleTimeScrubber", "Particle Time Scrubber", true)]
    internal sealed partial class ParticleTimeScrubberOverlay : Overlay
    {
        private const float PanelWidth = 320f;

        private IMGUIContainer container;
        private bool showColors;
        private Vector2 colorScroll;

        public override VisualElement CreatePanelContent()
        {
            container = new IMGUIContainer(OnPanelGUI);
            container.style.minWidth = PanelWidth;
            return container;
        }

        public override void OnCreated()
        {
            ParticleScrubController.Attach();
            ParticleScrubController.Changed += OnControllerChanged;
            OnControllerChanged();
        }

        public override void OnWillBeDestroyed()
        {
            ParticleScrubController.Changed -= OnControllerChanged;
            ParticleScrubController.Detach();
        }

        private void OnControllerChanged()
        {
            // 標準の Particle Effect パネルと同じ挙動: 対象があるときだけ姿を見せる。
            displayed = ParticleScrubController.Root != null;
            container?.MarkDirtyRepaint();
        }

        // ---------------------------------------------------------------
        // GUI
        // ---------------------------------------------------------------

        private void OnPanelGUI()
        {
            ParticleSystem root = ParticleScrubController.Root;
            if (root == null)
            {
                GUILayout.Label(
                    L10n.T("ParticleSystem を選択してください。", "Select a ParticleSystem."),
                    EditorStyles.miniLabel);
                return;
            }

            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 80f;
            try
            {
                DrawHeader(root);
                DrawTimeBar();
                DrawTransportButtons();
                DrawColorSection(root);
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
            }
        }

        private void DrawHeader(ParticleSystem root)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(root.name, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                L10n.IsEnglish = GUILayout.Toggle(
                    L10n.IsEnglish, "EN", EditorStyles.miniButton, GUILayout.Width(36f));
            }
        }

        private void DrawTimeBar()
        {
            EditorGUI.BeginChangeCheck();
            float newTime = EditorGUILayout.Slider(
                L10n.T("時刻 (秒)", "Time (s)"),
                ParticleScrubController.CurrentTime, 0f, ParticleScrubController.MaxTime);
            if (EditorGUI.EndChangeCheck())
            {
                // バーを触ったら再生は止める。掴んだままの微調整を優先する。
                ParticleScrubController.Scrub(newTime);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                float newMax = EditorGUILayout.FloatField(
                    L10n.T("最大時刻", "Max Time"), ParticleScrubController.MaxTime);
                if (EditorGUI.EndChangeCheck() && newMax > 0.01f)
                {
                    ParticleScrubController.MaxTime = newMax;
                    if (ParticleScrubController.CurrentTime > newMax)
                        ParticleScrubController.SetTime(newMax);
                }

                if (GUILayout.Button(L10n.T("自動", "Auto"), GUILayout.Width(50f)))
                {
                    ParticleScrubController.MaxTime =
                        ParticleScrubController.EstimateDuration(ParticleScrubController.Root);
                    if (ParticleScrubController.CurrentTime > ParticleScrubController.MaxTime)
                        ParticleScrubController.SetTime(ParticleScrubController.MaxTime);
                }
            }
        }

        private void DrawTransportButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(L10n.T("|◀ 先頭", "|◀ Start"), GUILayout.Height(22f)))
                {
                    ParticleScrubController.ToStart();
                }

                bool playing = ParticleScrubController.IsPlaying;
                string playLabel = playing
                    ? L10n.T("❚❚ 一時停止", "❚❚ Pause")
                    : L10n.T("▶ 再生", "▶ Play");
                if (GUILayout.Button(playLabel, GUILayout.Height(22f)))
                {
                    if (playing) ParticleScrubController.Pause();
                    else ParticleScrubController.Play();
                }

                ParticleScrubController.LoopPlayback = GUILayout.Toggle(
                    ParticleScrubController.LoopPlayback, L10n.T("ループ", "Loop"),
                    EditorStyles.miniButton, GUILayout.Width(50f), GUILayout.Height(22f));

                showColors = GUILayout.Toggle(
                    showColors, L10n.T("色変更", "Colors"),
                    EditorStyles.miniButton, GUILayout.Width(56f), GUILayout.Height(22f));
            }
        }

        internal static class L10n
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
