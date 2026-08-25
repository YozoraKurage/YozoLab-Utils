using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YozoLab.ParticleTools
{
    /// <summary>
    /// 選択に追従して対象エフェクトを確保し、時刻指定シミュレートを行う本体。
    /// GUI は持たない。Scene ビューのオーバーレイ(ParticleScrubberOverlay)と
    /// 色変更ウィンドウ(ParticleColorWindow)から操作される。
    ///
    /// 動かし方:
    ///   標準の Particle Effect パネルと同じ内部 API(BuiltinPreviewBridge)へ
    ///   時刻を渡し、ネイティブ側に積み直させる。ParticleSystem.Simulate を
    ///   C# から呼ぶ方式は、サブエミッタの受け側にも直接 Simulate をかけてしまい
    ///   親のイベントで出た粒が壊れるため使わない(内部 API を掴めない環境向けの
    ///   代替としてだけ残してある。そちらではサブエミッタを親に任せて除外する)。
    ///
    /// 決定論の作り方:
    ///   確保時に全 ParticleSystem の useAutoRandomSeed を切り、固定シードを与える。
    ///   時刻指定は常に「先頭から積み直す」ので、同じ時刻を指定すれば毎回同じ絵に
    ///   なる。元のシード設定は解放時に戻す。
    /// </summary>
    internal static class ParticleScrubController
    {
        private sealed class SeedRecord
        {
            public ParticleSystem Ps;
            public bool OriginalAutoSeed;
            public uint OriginalSeed;
        }

        public static ParticleSystem Root { get; private set; }
        public static float CurrentTime { get; private set; }
        public static float MaxTime { get; set; } = 5f;
        public static bool IsPlaying { get; private set; }
        public static bool LoopPlayback { get; set; } = true;

        /// <summary>再生速度の倍率。標準パネルの Playback Speed に相当する。</summary>
        public static float PlaybackSpeed { get; set; } = 1f;

        /// <summary>エフェクト全体の境界ボックスを Scene ビューへ描くか。</summary>
        public static bool ShowBounds { get; set; }

        /// <summary>時刻・対象・再生状態が変わったとき。オーバーレイやウィンドウが再描画に使う。</summary>
        public static event Action Changed;

        // ---------------------------------------------------------------
        // 設定(EditorPrefs 保存)
        // ---------------------------------------------------------------

        private const string AutoResimulatePref = "YozoLab_ParticleTools_AutoResimulate";
        private const string HideBuiltinPanelPref = "YozoLab_ParticleTools_HideBuiltinPanel";

        /// <summary>
        /// Inspector や色ウィンドウでの変更を、再生位置を保ったまま反映するか。
        /// 標準パネルの Resimulate に相当するが、こちらは間引いて掛け直す。
        /// </summary>
        public static bool AutoResimulate
        {
            get => EditorPrefs.GetBool(AutoResimulatePref, true);
            set => EditorPrefs.SetBool(AutoResimulatePref, value);
        }

        /// <summary>掴んでいる間、標準の Particle Effect パネルを隠すか。</summary>
        public static bool HideBuiltinPanel
        {
            get => EditorPrefs.GetBool(HideBuiltinPanelPref, true);
            set => EditorPrefs.SetBool(HideBuiltinPanelPref, value);
        }

        /// <summary>今このフレーム、標準パネルを隠すべきか(Harmony パッチから読まれる)。</summary>
        public static bool SuppressesBuiltinPanel => Root != null && HideBuiltinPanel;

        /// <summary>サブエミッタが正しく出る経路で動いているか。false なら代替経路。</summary>
        public static bool DrivesNativePreview => UsesNativeDriver;

        /// <summary>
        /// 内部 API 自体は掴めているのに、ネイティブ側が動かないので降りた状態か。
        /// (掴めていない場合と原因が別なので、表示を分けるために公開する)
        /// </summary>
        public static bool NativeDriverGaveUp => BuiltinPreviewBridge.CanDrivePlayback && nativeDriveFailed;

        /// <summary>ネイティブ経路をもう一度試す。オーバーレイの「再試行」から呼ばれる。</summary>
        public static void RetryNativeDriver()
        {
            if (!BuiltinPreviewBridge.CanDrivePlayback) return;

            nativeDriveFailed = false;
            nativeStallSince = 0.0;
            if (Root == null) return;

            SetTime(CurrentTime);
            if (IsPlaying) StartPlayback();
            NotifyChanged();
        }

        /// <summary>
        /// 内部 API を掴めていて、かつ実際にネイティブ側が動いているか。
        ///
        /// 掴めていても効かない場合がある。標準のプレビューはエディタの選択から
        /// 対象エフェクトを決めるので、選択が ParticleSystem そのものではなく
        /// エフェクト内の別オブジェクトだと、ネイティブ側は何も掴んでいない。
        /// そのときは時間が進まないので、見張って代替経路へ移る(TickPlayback)。
        /// </summary>
        private static bool UsesNativeDriver => BuiltinPreviewBridge.CanDrivePlayback && !nativeDriveFailed;

        // ---------------------------------------------------------------
        // 再シミュレートの間引き
        // ---------------------------------------------------------------

        // 変更が止まってからこれだけ待って掛け直す。ドラッグ中の連打を 1 回にまとめる。
        private const double SettleDelay = 0.06;

        // ただし変更が続いている間も、これだけ経ったら 1 回は掛け直す(反応が死なないように)。
        private const double MaxResyncInterval = 0.25;

        // 再生中の通知(=オーバーレイの再描画)はこの間隔まで。バーの数字は
        // 毎フレーム更新しなくても足りるうえ、IMGUI の再描画が重なると効いてくる。
        private const double NotifyInterval = 1.0 / 20.0;

        // 再生を頼んだのにネイティブ側の時刻がこれだけ動かなければ、掴めていないと見なす。
        private const double NativeStallTimeout = 1.0;

        // Scene ビューの再描画がこれだけ途切れていたら、シーンの更新自体が
        // 回っていないと見なす(上の判定を保留する)。
        private const double SceneIdleGrace = 0.2;

        // 再生開始からこの回数だけ Scene ビューが描かれるまでは失速と判定しない。
        // 初回のもたつきで誤って代替経路へ落ちないようにするための下駄。
        private const int MinSceneGuiBeforeStallCheck = 3;

        private static readonly List<SeedRecord> seedRecords = new List<SeedRecord>();
        private static readonly List<ParticleSystem> standaloneSystems = new List<ParticleSystem>();
        private static ParticleSystem[] systems = Array.Empty<ParticleSystem>();
        private static ParticleSystemRenderer[] renderers = Array.Empty<ParticleSystemRenderer>();

        private static double lastEditorTime;
        private static double lastModificationTime;
        private static double lastResyncTime;
        private static double lastNotifyTime;
        private static double nativeStallSince;
        private static double lastSceneGuiTime;
        private static int sceneGuiSincePlay;
        private static float lastNativeTimeSample;
        private static bool nativeDriveFailed;
        private static bool resyncPending;
        private static int attachCount;

        /// <summary>親のサブエミッタとして駆動される(=直接触ってはいけない)システムの数。</summary>
        public static int SubEmitterCount => systems.Length - standaloneSystems.Count;

        // ---------------------------------------------------------------
        // オーバーレイからのライフサイクル
        // ---------------------------------------------------------------

        /// <summary>Scene ビュー(オーバーレイ)1 つにつき 1 回呼ぶ。初回だけ本当に起動する。</summary>
        public static void Attach()
        {
            if (attachCount++ > 0) return;

            EditorApplication.update += OnEditorUpdate;
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseTarget;
            Undo.postprocessModifications += OnPostprocessModifications;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            SceneView.duringSceneGui += OnSceneGUI;

            BuiltinPanelSuppressor.Install();
            OnSelectionChanged();
        }

        public static void Detach()
        {
            if (--attachCount > 0) return;

            EditorApplication.update -= OnEditorUpdate;
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= ReleaseTarget;
            Undo.postprocessModifications -= OnPostprocessModifications;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            SceneView.duringSceneGui -= OnSceneGUI;

            ReleaseTarget();
            BuiltinPanelSuppressor.Uninstall();
        }

        // ---------------------------------------------------------------
        // 選択への追従
        // ---------------------------------------------------------------

        /// <summary>
        /// 選択オブジェクトから対象エフェクトのルートを決める。自分か祖先で
        /// 一番近い ParticleSystem を見つけ、そこから「親も ParticleSystem である
        /// 限り」上へたどった先をルートとする。
        ///
        /// この束ね方は Unity 内部の ParticleSystemEditorUtils.GetRoot と同じ。
        /// ネイティブ側のプレビューに時刻を渡す以上、どこまでを 1 つのエフェクトと
        /// 見るかが標準とずれていると、掴んだつもりのものと動くものが食い違う。
        /// </summary>
        public static ParticleSystem ResolveEffectRoot(GameObject gameObject)
        {
            if (gameObject == null || !gameObject.scene.IsValid()) return null;

            Transform nearest = null;
            for (Transform t = gameObject.transform; t != null; t = t.parent)
            {
                if (t.TryGetComponent(out ParticleSystem _))
                {
                    nearest = t;
                    break;
                }
            }
            if (nearest == null) return null;

            while (nearest.parent != null && nearest.parent.TryGetComponent(out ParticleSystem _))
            {
                nearest = nearest.parent;
            }
            return nearest.GetComponent<ParticleSystem>();
        }

        private static void OnSelectionChanged()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            ParticleSystem resolved = ResolveEffectRoot(Selection.activeGameObject);
            if (resolved == Root) return;

            if (resolved == null) ReleaseTarget();
            else AcquireTarget(resolved);

            NotifyChanged();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            // Play モードに入る前に必ず手を放す。シードの書き換えを残したまま
            // 実行に入ると、ゲーム側の見え方まで固定されてしまう。
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                ReleaseTarget();
                NotifyChanged();
            }
        }

        // ---------------------------------------------------------------
        // 対象の確保・解放
        // ---------------------------------------------------------------

        private static void AcquireTarget(ParticleSystem newRoot)
        {
            ReleaseTarget();

            // 対象ごとに見直す。前の対象で掴めなかっただけかもしれない。
            nativeDriveFailed = false;

            Root = newRoot;
            systems = Root.GetComponentsInChildren<ParticleSystem>(true);
            renderers = Root.GetComponentsInChildren<ParticleSystemRenderer>(true);
            CollectStandaloneSystems();

            Root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                seedRecords.Add(new SeedRecord
                {
                    Ps = ps,
                    OriginalAutoSeed = ps.useAutoRandomSeed,
                    OriginalSeed = ps.randomSeed,
                });

                // 手動シードが既に入っているならそれ自体が決定論的なので尊重する。
                if (ps.useAutoRandomSeed)
                {
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.useAutoRandomSeed = false;
                    ps.randomSeed = FixedSeed(i);
                }
            }

            MaxTime = EstimateDuration(Root);
            BuiltinPreviewBridge.BeginOwnership();
            SetTime(0f);

            // 選んだ直後から動いて見えるように、既定は再生から入る。
            StartPlayback();
        }

        /// <summary>シードを元へ戻し、シミュレート結果を消して手を放す。何度呼んでも安全。</summary>
        public static void ReleaseTarget()
        {
            IsPlaying = false;
            resyncPending = false;

            foreach (SeedRecord record in seedRecords)
            {
                if (record.Ps == null) continue;

                record.Ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                record.Ps.useAutoRandomSeed = record.OriginalAutoSeed;
                record.Ps.randomSeed = record.OriginalSeed;
            }
            seedRecords.Clear();

            if (Root != null)
            {
                Root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                SceneView.RepaintAll();
            }

            BuiltinPreviewBridge.EndOwnership();

            standaloneSystems.Clear();
            systems = Array.Empty<ParticleSystem>();
            renderers = Array.Empty<ParticleSystemRenderer>();
            Root = null;
        }

        /// <summary>
        /// 親のサブエミッタとして駆動されるシステムを除いた一覧を作る。
        ///
        /// サブエミッタの受け側は「親の粒が条件を満たしたときに親が出す」システムで、
        /// 自分で再生されることを前提にしていない。ここへ直接 Simulate をかけると
        /// 親のイベントで生まれた粒が消えたり二重に歳を取ったりするうえ、
        /// 受け側自身の Emission まで余計に走って粒が増える。
        /// 代替経路(Simulate 方式)ではこの一覧だけを進め、受け側は親に任せる。
        /// </summary>
        private static void CollectStandaloneSystems()
        {
            standaloneSystems.Clear();

            var drivenByParent = new HashSet<ParticleSystem>();
            foreach (ParticleSystem ps in systems)
            {
                ParticleSystem.SubEmittersModule sub = ps.subEmitters;
                if (!sub.enabled) continue;

                for (int i = 0; i < sub.subEmittersCount; i++)
                {
                    ParticleSystem emitted = sub.GetSubEmitterSystem(i);
                    if (emitted != null) drivenByParent.Add(emitted);
                }
            }

            foreach (ParticleSystem ps in systems)
            {
                if (!drivenByParent.Contains(ps)) standaloneSystems.Add(ps);
            }
        }

        /// <summary>インデックスから決めるだけの固定シード。0 は自動シード扱いになり得るので避ける。</summary>
        private static uint FixedSeed(int index) => 0x9E3779B9u * (uint)(index + 1);

        /// <summary>
        /// エフェクト全体の見た目上の長さを見積もる。ループするエフェクトに「終わり」は
        /// 無いので、1 周 + 最後に出た粒が消えるまでを目安として返す。バーの上限は
        /// オーバーレイからいつでも手で変えられるため、厳密である必要はない。
        /// </summary>
        public static float EstimateDuration(ParticleSystem system)
        {
            float result = 0.5f;
            foreach (ParticleSystem ps in system.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                float total = main.startDelay.constantMax + main.duration + main.startLifetime.constantMax;
                if (total > result) result = total;
            }
            return Mathf.Ceil(result * 10f) / 10f;
        }

        /// <summary>今シミュレートされている粒の総数。標準パネルの Particles 表示に相当する。</summary>
        public static int TotalParticleCount()
        {
            int count = 0;
            foreach (ParticleSystem ps in systems)
            {
                if (ps != null) count += ps.particleCount;
            }
            return count;
        }

        // ---------------------------------------------------------------
        // 再生操作
        // ---------------------------------------------------------------

        /// <summary>
        /// 再生位置を指定時刻へ移す。先頭から積み直すので同じ時刻は必ず同じ絵になる。
        /// 時刻が長いほどコストは伸びる。
        /// </summary>
        public static void SetTime(float time)
        {
            CurrentTime = Mathf.Clamp(time, 0f, MaxTime);
            if (Root == null) return;

            Resimulate();
            SceneView.RepaintAll();
            NotifyChanged();
        }

        /// <summary>CurrentTime の絵を作り直す。経路の違いを吸収するのはここだけ。</summary>
        private static void Resimulate()
        {
            lastResyncTime = EditorApplication.timeSinceStartup;

            if (UsesNativeDriver)
            {
                // 標準パネルの Playback Time と同じ手順。停止状態のままだと
                // ネイティブ側が再シミュレートの要求を捨てるので、
                // 一度 Play → Pause して「止まっているが生きている」状態にする。
                if (Root.isStopped)
                {
                    Root.Play();
                    Root.Pause();
                }

                BuiltinPreviewBridge.IsScrubbing = true;
                BuiltinPreviewBridge.PlaybackTime = CurrentTime;
                BuiltinPreviewBridge.Resimulate();
                return;
            }

            FallbackResimulate();
        }

        public static void Play()
        {
            if (Root == null) return;
            if (CurrentTime >= MaxTime) SetTime(0f);
            StartPlayback();
            NotifyChanged();
        }

        private static void StartPlayback()
        {
            lastEditorTime = EditorApplication.timeSinceStartup;
            IsPlaying = true;

            if (!UsesNativeDriver) return;

            // 進んでいるかの見張りをここから数え直す。
            lastNativeTimeSample = CurrentTime;
            nativeStallSince = 0.0;
            sceneGuiSincePlay = 0;

            BuiltinPreviewBridge.SimulationSpeed = Mathf.Max(0f, PlaybackSpeed);
            BuiltinPreviewBridge.IsScrubbing = false;

            // playbackIsPlaying / playbackIsPaused は書かない。Unity 自身も書かず、
            // ネイティブ状態の読み取りとして使っている。再生を始めるのは Play() の方。
            //
            // ここは無条件に呼ぶ必要がある。直前の積み直しが Play → Pause で
            // 「止まっているが生きている」状態を作っているので、isStopped は false。
            // 条件付きにすると一時停止のまま放置され、時刻が一切進まない。
            if (Root != null) Root.Play();
        }

        public static void Pause()
        {
            IsPlaying = false;

            if (UsesNativeDriver && Root != null)
            {
                // 標準パネルの Pause と同じ: 実体を止めて、スクラブ中の印を立てる。
                Root.Pause();
                BuiltinPreviewBridge.IsScrubbing = true;
            }

            // 停止時に決定論的な絵へ揃え直す。再生中は経過時間なりに進むので、
            // 止まった瞬間の絵を「その時刻をバーで指定したときと同じ絵」に保証する。
            SetTime(CurrentTime);
        }

        /// <summary>標準パネルの Stop に相当。粒を消して時刻 0 で止まる(シミュレートもしない)。</summary>
        public static void Stop()
        {
            IsPlaying = false;
            CurrentTime = 0f;

            if (Root != null)
            {
                if (UsesNativeDriver)
                {
                    // 標準パネルの Stop と同じ 3 手順。
                    BuiltinPreviewBridge.IsScrubbing = false;
                    BuiltinPreviewBridge.PlaybackTime = 0f;
                    BuiltinPreviewBridge.StopEffect();
                }

                Root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                SceneView.RepaintAll();
            }
            NotifyChanged();
        }

        /// <summary>バー操作用。再生を止めてから指定時刻へ移る(積み直しは 1 回だけ)。</summary>
        public static void Scrub(float time)
        {
            if (IsPlaying)
            {
                IsPlaying = false;
                if (UsesNativeDriver && Root != null)
                {
                    Root.Pause();
                    BuiltinPreviewBridge.IsScrubbing = true;
                }
            }
            SetTime(time);
        }

        /// <summary>標準パネルの Restart に相当。先頭から再生し直す。</summary>
        public static void Restart()
        {
            SetTime(0f);
            Play();
        }

        /// <summary>Inspector や色ウィンドウでの変更後に呼ぶ。間引いたうえで同時刻へ積み直す。</summary>
        public static void RequestResync()
        {
            resyncPending = true;
            lastModificationTime = EditorApplication.timeSinceStartup;
        }

        private static void OnEditorUpdate()
        {
            // Unity の == は破棄済みオブジェクトも null 扱いにするので、
            // 「未指定」と「破棄された」を参照比較で区別する。
            if (ReferenceEquals(Root, null)) return;

            // 対象が消された(シーンを閉じた・削除した)ら記録ごと片付ける。
            if (Root == null)
            {
                ReleaseTarget();
                NotifyChanged();
                return;
            }

            if (IsPlaying)
            {
                TickPlayback();
                return;
            }

            TickResync();
        }

        private static void TickPlayback()
        {
            double now = EditorApplication.timeSinceStartup;

            if (UsesNativeDriver)
            {
                // 時間を進めるのはネイティブ側。こちらは読むだけで、
                // バーの上限に達したときだけ折り返しを指示する。
                BuiltinPreviewBridge.SimulationSpeed = Mathf.Max(0f, PlaybackSpeed);

                float time = BuiltinPreviewBridge.PlaybackTime;
                if (!CheckNativeAdvance(time, now)) return;

                if (time >= MaxTime)
                {
                    if (LoopPlayback)
                    {
                        CurrentTime = 0f;
                        Resimulate();
                        StartPlayback();
                    }
                    else
                    {
                        IsPlaying = false;
                        Root.Pause();
                        BuiltinPreviewBridge.IsScrubbing = true;
                        SetTime(MaxTime);
                        return;
                    }
                }
                else
                {
                    CurrentTime = time;
                }

                SceneView.RepaintAll();
                ThrottledNotify(now);
                return;
            }

            // 代替経路: 標準の再生と取り合いになるので毎回止めて主導権を取り続ける。
            BuiltinPreviewBridge.PausePlayback();

            float delta = (float)(now - lastEditorTime) * Mathf.Max(0f, PlaybackSpeed);
            lastEditorTime = now;

            CurrentTime += delta;
            if (CurrentTime >= MaxTime)
            {
                if (LoopPlayback)
                {
                    SetTime(CurrentTime - MaxTime);
                }
                else
                {
                    IsPlaying = false;
                    SetTime(MaxTime);
                }
                return;
            }

            // 再生中は差分だけ進める。毎フレーム先頭から積み直すと
            // 時刻に比例して重くなり、再生が破綻するため。
            FallbackAdvance(delta);
            SceneView.RepaintAll();
            ThrottledNotify(now);
        }

        /// <summary>
        /// 再生を頼んだのにネイティブ側の時刻が動かないなら、標準のプレビューが
        /// このエフェクトを掴んでいない(選択が ParticleSystem そのものでない等)。
        /// 気づかないとバーが凍るだけなので、代替経路へ移して true 以外を返す。
        /// </summary>
        private static bool CheckNativeAdvance(float time, double now)
        {
            // 速度 0 は「止めている」ので、進まなくても異常ではない。
            if (PlaybackSpeed <= 0f || !Mathf.Approximately(time, lastNativeTimeSample))
            {
                lastNativeTimeSample = time;
                nativeStallSince = 0.0;
                return true;
            }

            // Scene ビューが再描画されていない(別タブの裏に回っている等)なら、
            // シーンの更新自体が回っていないので進まなくて当たり前。判定しない。
            // 再生を始めた直後の数フレームも同じ理由で見送る。
            if (now - lastSceneGuiTime > SceneIdleGrace
                || sceneGuiSincePlay < MinSceneGuiBeforeStallCheck)
            {
                nativeStallSince = 0.0;
                return true;
            }

            if (nativeStallSince <= 0.0)
            {
                nativeStallSince = now;
                return true;
            }
            if (now - nativeStallSince < NativeStallTimeout) return true;

            nativeDriveFailed = true;
            nativeStallSince = 0.0;
            BuiltinPreviewBridge.PausePlayback();
            lastEditorTime = now;
            Resimulate();
            SceneView.RepaintAll();
            NotifyChanged();
            return false;
        }

        /// <summary>
        /// 溜まった変更を、間引いてから 1 回の積み直しにまとめる。
        ///
        /// 素直に「変更があったら積み直す」と、Inspector のスライダを掴んでいる間は
        /// GUI イベントごとに 0 秒目からの全ステップが走る。1 回が数十ミリ秒あるので
        /// ドラッグしている間ずっとシーンが詰まる。変更が収まるのを少しだけ待ち、
        /// 待ちきれないときも一定間隔に落とす。
        /// </summary>
        private static void TickResync()
        {
            if (!resyncPending) return;

            if (!AutoResimulate)
            {
                resyncPending = false;
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            bool settled = now - lastModificationTime >= SettleDelay;
            bool overdue = now - lastResyncTime >= MaxResyncInterval;
            if (!settled && !overdue) return;

            resyncPending = false;
            SetTime(CurrentTime);
        }

        // ---------------------------------------------------------------
        // 内部 API を掴めない環境向けの代替
        // ---------------------------------------------------------------

        private static void FallbackResimulate()
        {
            // サブエミッタの受け側も含めて一度きれいに消してから積み直す。
            Root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            SimulateStandalone(CurrentTime, restart: true);
        }

        private static void FallbackAdvance(float delta)
        {
            SimulateStandalone(delta, restart: false);
        }

        /// <summary>
        /// 独立して動くシステムだけを個別に進める。withChildren は使わない
        /// (Unity がサブエミッタの受け側にも Simulate をかけてしまうため)。
        /// 深い側から進めるのは、親のイベントで生まれた粒がその回のうちに
        /// もう一度進まないようにするため。
        /// </summary>
        private static void SimulateStandalone(float time, bool restart)
        {
            for (int i = standaloneSystems.Count - 1; i >= 0; i--)
            {
                ParticleSystem ps = standaloneSystems[i];
                if (ps == null) continue;

                ps.Simulate(time, withChildren: false, restart: restart, fixedTimeStep: true);
            }
        }

        // ---------------------------------------------------------------
        // 通知
        // ---------------------------------------------------------------

        private static void NotifyChanged()
        {
            lastNotifyTime = EditorApplication.timeSinceStartup;
            Changed?.Invoke();
        }

        private static void ThrottledNotify(double now)
        {
            if (now - lastNotifyTime < NotifyInterval) return;
            lastNotifyTime = now;
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------
        // 境界の表示
        // ---------------------------------------------------------------

        private static void OnSceneGUI(SceneView sceneView)
        {
            // Scene ビューが今も描かれていることの目印。ネイティブ側が時間を
            // 進めているかの判定(CheckNativeAdvance)で使う。
            lastSceneGuiTime = EditorApplication.timeSinceStartup;
            if (sceneGuiSincePlay < MinSceneGuiBeforeStallCheck) sceneGuiSincePlay++;

            if (!ShowBounds || Root == null) return;

            Handles.color = new Color(1f, 0.92f, 0.3f, 0.9f);
            foreach (ParticleSystemRenderer renderer in renderers)
            {
                if (renderer == null) continue;

                Bounds bounds = renderer.bounds;
                Handles.DrawWireCube(bounds.center, bounds.size);
            }
        }

        // ---------------------------------------------------------------
        // Inspector 変更の検知
        // ---------------------------------------------------------------

        private static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            if (Root != null)
            {
                foreach (UndoPropertyModification modification in modifications)
                {
                    if (IsRelated(modification.currentValue?.target))
                    {
                        RequestResync();
                        break;
                    }
                }
            }
            return modifications;
        }

        private static void OnUndoRedoPerformed()
        {
            if (Root != null) RequestResync();
        }

        /// <summary>そのオブジェクトが対象エフェクトの階層(またはそこで使うマテリアル)か。</summary>
        private static bool IsRelated(UnityEngine.Object obj)
        {
            if (obj is Material) return true;

            Transform transform = obj switch
            {
                GameObject gameObject => gameObject.transform,
                Component component => component.transform,
                _ => null,
            };
            return transform != null && transform.IsChildOf(Root.transform);
        }
    }

    /// <summary>言語切替。パッケージ内の GUI で共用する。</summary>
    internal static class L10n
    {
        private const string PrefKey = "YozoLab_ParticleTools_Language";

        public static bool IsEnglish
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        public static string T(string jp, string en) => IsEnglish ? en : jp;
    }
}
