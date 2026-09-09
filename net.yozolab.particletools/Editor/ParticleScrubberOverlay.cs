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
                DrawUpdateOptions();
                DrawColorWindowRow();
                DrawFallbackNotice();
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

                int particles = ParticleScrubController.TotalParticleCount();
                int subEmitters = ParticleScrubController.SubEmitterCount;
                GUILayout.Label(
                    subEmitters > 0
                        ? L10n.T($"粒: {particles}(サブ {subEmitters})",
                                 $"Particles: {particles} (sub {subEmitters})")
                        : L10n.T($"粒: {particles}", $"Particles: {particles}"),
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

        private static void DrawUpdateOptions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                // 標準パネルの Resimulate に相当。ただしこちらは変更が収まってから
                // 1 回にまとめて掛け直すので、スライダを掴んでいる間もシーンが詰まらない。
                ParticleScrubController.AutoResimulate = GUILayout.Toggle(
                    ParticleScrubController.AutoResimulate,
                    new GUIContent(
                        L10n.T("即時反映", "Resimulate"),
                        L10n.T("Inspector での変更を、再生位置を保ったまま反映する。"
                               + "重いエフェクトを編集している間だけ切ると軽くなる。",
                               "Reflect Inspector edits at the current playback time. "
                               + "Turn off while editing a heavy effect to keep the scene responsive.")),
                    EditorStyles.miniButtonLeft);

                bool canHide = BuiltinPanelSuppressor.Available;
                using (new EditorGUI.DisabledScope(!canHide))
                {
                    bool hide = GUILayout.Toggle(
                        canHide && ParticleScrubController.HideBuiltinPanel,
                        new GUIContent(
                            L10n.T("標準パネルを隠す", "Hide Built-in"),
                            canHide
                                ? L10n.T("Unity 標準の Particle Effect パネルを隠す。",
                                         "Hide Unity's built-in Particle Effect panel.")
                                : L10n.T($"使えません: {BuiltinPanelSuppressor.UnavailableReason}",
                                         $"Unavailable: {BuiltinPanelSuppressor.UnavailableReason}")),
                        EditorStyles.miniButtonRight);

                    // 使えないときは押せないので、保存してある設定を上書きしない。
                    if (canHide) ParticleScrubController.HideBuiltinPanel = hide;
                }
            }
        }

        /// <summary>
        /// 代替経路で動いているときだけ出す注意書き。この状態ではサブエミッタの
        /// 受け側を親に任せる簡易シミュレートになるため、標準の見え方と差が出る。
        ///
        /// 理由は 2 通りあり、対処が違うので分けて出す。
        ///   - 内部 API を掴めなかった:  Unity 側の構成が変わった(打つ手なし)。
        ///   - 掴めたが時刻が進まない:  標準のプレビューがこのエフェクトを掴んで
        ///                              いない。選択を ParticleSystem 本体にすれば直る。
        /// </summary>
        private static void DrawFallbackNotice()
        {
            if (ParticleScrubController.DrivesNativePreview) return;

            if (ParticleScrubController.NativeDriverGaveUp)
            {
                EditorGUILayout.HelpBox(
                    L10n.T("標準のプレビューがこのエフェクトを掴んでいないため、簡易シミュレートで"
                           + "代用しています。ParticleSystem そのものを選択すると本来の経路に戻ります。",
                           "Unity's built-in preview is not driving this effect, so a plain simulation "
                           + "is used instead. Selecting the ParticleSystem itself restores the normal path."),
                    MessageType.Warning);

                if (GUILayout.Button(L10n.T("再試行", "Retry"), EditorStyles.miniButton))
                {
                    ParticleScrubController.RetryNativeDriver();
                }
                return;
            }

            EditorGUILayout.HelpBox(
                L10n.T("Unity 内部のプレビュー API を掴めませんでした。簡易シミュレートで代用しています"
                       + $"(サブエミッタの見え方が標準と異なる場合があります)。\n{BuiltinPreviewBridge.Diagnostics}",
                       "Could not reach Unity's internal preview API. Falling back to plain simulation "
                       + $"(sub-emitters may look different from the built-in preview).\n{BuiltinPreviewBridge.Diagnostics}"),
                MessageType.Warning);
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
