using UnityEditor;
using UnityEditor.Overlays;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace YozoLab.ParticleTools
{
    /// <summary>
    /// Scene ビューに出す操作パネル。Unity 標準の Particle Effect パネルの置き換えを
    /// 狙っていて、同じく ParticleSystem(またはその子)を選択すると現れ、選択を
    /// 外すと消える。標準パネルの機能(Pause / Restart / Stop / Playback Speed /
    /// Playback Time / Particles 数 / Simulate Layers / Show Bounds / Show Only
    /// Selected)を引き継ぎ、そこへ時間バーによるスクラブと決定論を足している。
    ///
    /// シミュレートや対象管理の実体は ParticleScrubController が持つ。
    /// 色の編集は独立した ParticleColorWindow が担当する。
    /// </summary>
    [Overlay(typeof(SceneView), "ParticleScrubber", "Particle Scrubber", true)]
    internal sealed class ParticleScrubberOverlay : Overlay
    {
        private const float PanelWidth = 300f;
        private const float TransportButtonWidth = 32f;

        private IMGUIContainer container;

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
            try
            {
                DrawHeader(root);
                DrawTimeBar();
                DrawTransportRow();
                DrawPreviewOptions();
                DrawColorWindowRow();
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
            }
        }

        private static void DrawHeader(ParticleSystem root)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(root.name, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(
                    L10n.T($"粒: {ParticleScrubController.TotalParticleCount()}",
                           $"Particles: {ParticleScrubController.TotalParticleCount()}"),
                    EditorStyles.miniLabel);
                L10n.IsEnglish = GUILayout.Toggle(
                    L10n.IsEnglish, "EN", EditorStyles.miniButton, GUILayout.Width(32f));
            }
        }

        private static void DrawTimeBar()
        {
            // バーはラベル無しで全幅を使う。数値での指定は下の行で行う。
            EditorGUI.BeginChangeCheck();
            float newTime = EditorGUILayout.Slider(
                ParticleScrubController.CurrentTime, 0f, ParticleScrubController.MaxTime);
            if (EditorGUI.EndChangeCheck())
            {
                ParticleScrubController.Scrub(newTime);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUIUtility.labelWidth = 34f;

                EditorGUI.BeginChangeCheck();
                float typedTime = EditorGUILayout.FloatField(
                    L10n.T("時刻", "Time"), ParticleScrubController.CurrentTime);
                if (EditorGUI.EndChangeCheck())
                {
                    ParticleScrubController.Scrub(typedTime);
                }

                EditorGUI.BeginChangeCheck();
                float newMax = EditorGUILayout.FloatField(
                    L10n.T("最大", "Max"), ParticleScrubController.MaxTime);
                if (EditorGUI.EndChangeCheck() && newMax > 0.01f)
                {
                    ParticleScrubController.MaxTime = newMax;
                    if (ParticleScrubController.CurrentTime > newMax)
                        ParticleScrubController.SetTime(newMax);
                }

                if (GUILayout.Button(L10n.T("自動", "Auto"), EditorStyles.miniButton, GUILayout.Width(40f)))
                {
                    ParticleScrubController.MaxTime =
                        ParticleScrubController.EstimateDuration(ParticleScrubController.Root);
                    if (ParticleScrubController.CurrentTime > ParticleScrubController.MaxTime)
                        ParticleScrubController.SetTime(ParticleScrubController.MaxTime);
                }
            }
        }

        private static void DrawTransportRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool playing = ParticleScrubController.IsPlaying;

                // 再生中なら先頭からの再生し直し(標準パネルの Restart)、
                // 停止中なら先頭へ戻るだけ。
                if (GUILayout.Button("|◀", EditorStyles.miniButtonLeft, GUILayout.Width(TransportButtonWidth)))
                {
                    if (playing) ParticleScrubController.Restart();
                    else ParticleScrubController.Scrub(0f);
                }

                if (GUILayout.Button(playing ? "❚❚" : "▶",
                        EditorStyles.miniButtonMid, GUILayout.Width(TransportButtonWidth)))
                {
                    if (playing) ParticleScrubController.Pause();
                    else ParticleScrubController.Play();
                }

                // 標準パネルの Stop: 粒を消して時刻 0 で待機。
                if (GUILayout.Button("■", EditorStyles.miniButtonRight, GUILayout.Width(TransportButtonWidth)))
                {
                    ParticleScrubController.Stop();
                }

                GUILayout.Space(6f);

                EditorGUIUtility.labelWidth = 34f;
                float newSpeed = EditorGUILayout.FloatField(
                    L10n.T("速度", "Speed"), ParticleScrubController.PlaybackSpeed, GUILayout.Width(70f));
                ParticleScrubController.PlaybackSpeed = Mathf.Max(0f, newSpeed);

                GUILayout.FlexibleSpace();

                ParticleScrubController.LoopPlayback = GUILayout.Toggle(
                    ParticleScrubController.LoopPlayback, L10n.T("ループ", "Loop"),
                    EditorStyles.miniButton, GUILayout.Width(48f));
            }
        }

        private static void DrawPreviewOptions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                ParticleScrubController.ShowBounds = GUILayout.Toggle(
                    ParticleScrubController.ShowBounds,
                    L10n.T("境界", "Bounds"), EditorStyles.miniButtonLeft);

                if (BuiltinPreviewBridge.HasShowOnlySelected)
                {
                    BuiltinPreviewBridge.ShowOnlySelected = GUILayout.Toggle(
                        BuiltinPreviewBridge.ShowOnlySelected,
                        L10n.T("選択のみ", "Only Selected"), EditorStyles.miniButtonRight);
                }

                if (BuiltinPreviewBridge.HasPreviewLayers)
                {
                    GUILayout.Space(6f);
                    int concatenated = InternalEditorUtility.LayerMaskToConcatenatedLayersMask(
                        unchecked((int)BuiltinPreviewBridge.PreviewLayers));
                    EditorGUI.BeginChangeCheck();
                    int newConcatenated = EditorGUILayout.MaskField(
                        concatenated, InternalEditorUtility.layers);
                    if (EditorGUI.EndChangeCheck())
                    {
                        BuiltinPreviewBridge.PreviewLayers = unchecked((uint)(int)
                            InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(newConcatenated));
                        ParticleScrubController.RequestResync();
                    }
                }
            }
        }

        private static void DrawColorWindowRow()
        {
            // 色の編集は時間操作とは別の機能なので、独立した行と独立したウィンドウにする。
            if (GUILayout.Button(L10n.T("色を編集...", "Edit Colors..."), EditorStyles.miniButton))
            {
                ParticleColorWindow.ShowWindow();
            }
        }
    }
}
