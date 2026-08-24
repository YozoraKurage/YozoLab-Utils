using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace YozoLab.ParticleTimeScrubber
{
    /// <summary>
    /// 選択に追従して対象エフェクトを確保し、時刻指定シミュレートを行う本体。
    /// GUI は持たない。Scene ビューのオーバーレイ(ParticleTimeScrubberOverlay)から操作される。
    ///
    /// 決定論の作り方:
    ///   確保時に全 ParticleSystem の useAutoRandomSeed を切り、固定シードを与える。
    ///   時刻指定は常に「先頭から固定タイムステップで再シミュレート」するので、
    ///   同じ時刻を指定すれば毎回同じ絵になる。元のシード設定は解放時に戻す。
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

        /// <summary>時刻・対象・再生状態が変わったとき。オーバーレイが再描画に使う。</summary>
        public static event Action Changed;

        private static readonly List<SeedRecord> seedRecords = new List<SeedRecord>();
        private static double lastEditorTime;
        private static bool resyncPending;
        private static int attachCount;

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

            ReleaseTarget();
        }

        // ---------------------------------------------------------------
        // 選択への追従
        // ---------------------------------------------------------------

        /// <summary>
        /// 選択オブジェクトから対象エフェクトのルートを決める。自分か祖先に
        /// ParticleSystem があれば、その最上位のものをルートとする。
        /// </summary>
        public static ParticleSystem ResolveEffectRoot(GameObject gameObject)
        {
            if (gameObject == null || !gameObject.scene.IsValid()) return null;

            ParticleSystem topmost = null;
            for (Transform t = gameObject.transform; t != null; t = t.parent)
            {
                if (t.TryGetComponent(out ParticleSystem ps)) topmost = ps;
            }
            return topmost;
        }

        private static void OnSelectionChanged()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            ParticleSystem resolved = ResolveEffectRoot(Selection.activeGameObject);
            if (resolved == Root) return;

            if (resolved == null) ReleaseTarget();
            else AcquireTarget(resolved);

            Changed?.Invoke();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            // Play モードに入る前に必ず手を放す。シードの書き換えを残したまま
            // 実行に入ると、ゲーム側の見え方まで固定されてしまう。
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                ReleaseTarget();
                Changed?.Invoke();
            }
        }

        // ---------------------------------------------------------------
        // 対象の確保・解放
        // ---------------------------------------------------------------

        private static void AcquireTarget(ParticleSystem newRoot)
        {
            ReleaseTarget();

            Root = newRoot;
            Root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem[] all = Root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < all.Length; i++)
            {
                ParticleSystem ps = all[i];
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
            BuiltinPreview.Pause();
            SetTime(0f);

            // 選んだ直後から動いて見えるように、既定は再生から入る。
            lastEditorTime = EditorApplication.timeSinceStartup;
            IsPlaying = true;
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
            Root = null;
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

        // ---------------------------------------------------------------
        // 再生操作
        // ---------------------------------------------------------------

        /// <summary>
        /// 再生位置を指定時刻へ移す。先頭から固定タイムステップで積み直すので
        /// 同じ時刻は必ず同じ絵になる。時刻が長いほどコストは伸びる。
        /// </summary>
        public static void SetTime(float time)
        {
            CurrentTime = Mathf.Clamp(time, 0f, MaxTime);
            if (Root == null) return;

            Root.Simulate(CurrentTime, withChildren: true, restart: true, fixedTimeStep: true);
            SceneView.RepaintAll();
            Changed?.Invoke();
        }

        public static void Play()
        {
            if (Root == null) return;
            if (CurrentTime >= MaxTime) SetTime(0f);
            lastEditorTime = EditorApplication.timeSinceStartup;
            IsPlaying = true;
            Changed?.Invoke();
        }

        public static void Pause()
        {
            IsPlaying = false;
            // 停止時に決定論的な絵へ揃え直す。再生中の逐次シミュレートは
            // フレーム間隔に依存するので、止まった瞬間の絵を
            // 「その時刻をバーで指定したときと同じ絵」に保証する。
            SetTime(CurrentTime);
        }

        /// <summary>バー操作用。再生を止めてから指定時刻へ移る(シミュレートは 1 回だけ)。</summary>
        public static void Scrub(float time)
        {
            IsPlaying = false;
            SetTime(time);
        }

        public static void ToStart()
        {
            IsPlaying = false;
            SetTime(0f);
        }

        /// <summary>Inspector や色パネルでの変更後に呼ぶ。次の update で同時刻へ再シミュレートする。</summary>
        public static void RequestResync() => resyncPending = true;

        private static void OnEditorUpdate()
        {
            // Unity の == は破棄済みオブジェクトも null 扱いにするので、
            // 「未指定」と「破棄された」を参照比較で区別する。
            if (ReferenceEquals(Root, null)) return;

            // 対象が消された(シーンを閉じた・削除した)ら記録ごと片付ける。
            if (Root == null)
            {
                ReleaseTarget();
                Changed?.Invoke();
                return;
            }

            // 対象を選択している間は標準のプレビューが同じエフェクトを動かそうと
            // するので、毎回止めて主導権を取り続ける。
            BuiltinPreview.Pause();

            if (IsPlaying)
            {
                double now = EditorApplication.timeSinceStartup;
                float delta = (float)(now - lastEditorTime);
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
                }
                else
                {
                    // 再生中は差分だけ進める。毎フレーム先頭から積み直すと
                    // 時刻に比例して重くなり、再生が破綻するため。
                    Root.Simulate(delta, withChildren: true, restart: false, fixedTimeStep: true);
                    SceneView.RepaintAll();
                    Changed?.Invoke();
                }
                return;
            }

            if (resyncPending)
            {
                resyncPending = false;
                SetTime(CurrentTime);
            }
        }

        // ---------------------------------------------------------------
        // Inspector 変更の検知
        // ---------------------------------------------------------------

        private static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            if (Root != null && !resyncPending)
            {
                foreach (UndoPropertyModification modification in modifications)
                {
                    if (IsRelated(modification.currentValue?.target))
                    {
                        resyncPending = true;
                        break;
                    }
                }
            }
            return modifications;
        }

        private static void OnUndoRedoPerformed()
        {
            if (Root != null) resyncPending = true;
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

        // ---------------------------------------------------------------
        // 標準プレビューの黙らせ役
        // ---------------------------------------------------------------

        /// <summary>
        /// ParticleSystem を選択すると、Unity 標準の Particle Effect パネルが
        /// 同じエフェクトのプレビュー再生を始め、こちらのシミュレートと毎フレーム
        /// 取り合いになる。標準側の再生フラグを内部 API 越しに落として回避する。
        /// リフレクションが外れた(将来の改名など)場合は、単に何もしない。
        /// </summary>
        private static class BuiltinPreview
        {
            private static readonly PropertyInfo playbackIsPlaying;

            static BuiltinPreview()
            {
                try
                {
                    Type utils = typeof(Editor).Assembly.GetType("UnityEditor.ParticleSystemEditorUtils");
                    const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    playbackIsPlaying = utils?.GetProperty("playbackIsPlaying", flags)
                                        ?? utils?.GetProperty("editorIsPlaying", flags);
                }
                catch
                {
                    playbackIsPlaying = null;
                }
            }

            public static void Pause()
            {
                try
                {
                    if (playbackIsPlaying != null && (bool)playbackIsPlaying.GetValue(null))
                        playbackIsPlaying.SetValue(null, false);
                }
                catch
                {
                    // 内部 API が変わっただけなら機能全体を巻き込まない。
                }
            }
        }
    }
}
