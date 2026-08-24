using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YozoLab.ParticleTimeScrubber
{
    /// <summary>
    /// 対象エフェクトの確保・解放と、時刻指定シミュレートの本体。
    ///
    /// 決定論の作り方:
    ///   確保時に全 ParticleSystem の useAutoRandomSeed を切り、固定シードを与える。
    ///   時刻指定は常に「先頭から固定タイムステップで再シミュレート」するので、
    ///   同じ時刻を指定すれば毎回同じ絵になる。元のシード設定は解放時に戻す。
    /// </summary>
    public partial class ParticleTimeScrubberWindow
    {
        private sealed class SeedRecord
        {
            public ParticleSystem Ps;
            public bool OriginalAutoSeed;
            public uint OriginalSeed;
        }

        private ParticleSystem root;
        private readonly List<SeedRecord> seedRecords = new List<SeedRecord>();

        private float currentTime;
        private float maxTime = 5f;
        private bool isPlaying;
        private bool loopPlayback;
        private double lastEditorTime;

        /// <summary>Inspector での変更を検知したら立て、次の update で再シミュレートする。</summary>
        private bool resyncPending;

        // ---------------------------------------------------------------
        // 対象の確保・解放
        // ---------------------------------------------------------------

        private void AcquireTarget(ParticleSystem newRoot)
        {
            ReleaseTarget();

            if (newRoot == null) return;

            // プレハブアセットそのものは Scene ビューに出ないので対象外。
            if (!newRoot.gameObject.scene.IsValid())
            {
                Debug.LogWarning("[Particle Time Scrubber] シーン上のインスタンスを指定してください。プレハブアセットは対象にできません。");
                return;
            }

            root = newRoot;
            root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem[] all = root.GetComponentsInChildren<ParticleSystem>(true);
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

            maxTime = EstimateDuration(root);
            isPlaying = false;
            SetTime(0f);
        }

        /// <summary>シードを元へ戻し、シミュレート結果を消して手を放す。何度呼んでも安全。</summary>
        private void ReleaseTarget()
        {
            isPlaying = false;
            resyncPending = false;

            foreach (SeedRecord record in seedRecords)
            {
                if (record.Ps == null) continue;

                record.Ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                record.Ps.useAutoRandomSeed = record.OriginalAutoSeed;
                record.Ps.randomSeed = record.OriginalSeed;
            }
            seedRecords.Clear();

            if (root != null)
            {
                root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                SceneView.RepaintAll();
            }
            root = null;
        }

        /// <summary>インデックスから決めるだけの固定シード。0 は自動シード扱いになり得るので避ける。</summary>
        private static uint FixedSeed(int index) => 0x9E3779B9u * (uint)(index + 1);

        /// <summary>
        /// エフェクト全体の見た目上の長さを見積もる。ループするエフェクトに「終わり」は
        /// 無いので、1 周 + 最後に出た粒が消えるまでを目安として返す。バーの上限は
        /// GUI からいつでも手で変えられるため、厳密である必要はない。
        /// </summary>
        private static float EstimateDuration(ParticleSystem system)
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
        // シミュレート
        // ---------------------------------------------------------------

        /// <summary>
        /// 再生位置を指定時刻へ移す。先頭から固定タイムステップで積み直すので
        /// 同じ時刻は必ず同じ絵になる。時刻が長いほどコストは伸びる。
        /// </summary>
        private void SetTime(float time)
        {
            currentTime = Mathf.Clamp(time, 0f, maxTime);
            if (root == null) return;

            root.Simulate(currentTime, withChildren: true, restart: true, fixedTimeStep: true);
            SceneView.RepaintAll();
            Repaint();
        }

        private void OnEditorUpdate()
        {
            // Unity の == は破棄済みオブジェクトも null 扱いにするので、
            // 「未指定」と「破棄された」を参照比較で区別する。
            if (ReferenceEquals(root, null)) return;

            // 対象が消された(シーンを閉じた・削除した)ら記録ごと片付ける。
            if (root == null)
            {
                ReleaseTarget();
                Repaint();
                return;
            }

            if (isPlaying)
            {
                double now = EditorApplication.timeSinceStartup;
                float delta = (float)(now - lastEditorTime);
                lastEditorTime = now;

                currentTime += delta;
                if (currentTime >= maxTime)
                {
                    if (loopPlayback)
                    {
                        SetTime(currentTime - maxTime);
                    }
                    else
                    {
                        isPlaying = false;
                        SetTime(maxTime);
                    }
                }
                else
                {
                    // 再生中は差分だけ進める。毎フレーム先頭から積み直すと
                    // 時刻に比例して重くなり、再生が破綻するため。
                    root.Simulate(delta, withChildren: true, restart: false, fixedTimeStep: true);
                    SceneView.RepaintAll();
                }
                Repaint();
                return;
            }

            if (resyncPending)
            {
                resyncPending = false;
                SetTime(currentTime);
            }
        }

        // ---------------------------------------------------------------
        // Inspector 変更の検知
        // ---------------------------------------------------------------

        private UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            if (root != null && !resyncPending)
            {
                foreach (UndoPropertyModification modification in modifications)
                {
                    if (IsUnderTarget(modification.currentValue?.target))
                    {
                        resyncPending = true;
                        break;
                    }
                }
            }
            return modifications;
        }

        private void OnUndoRedoPerformed()
        {
            if (root != null) resyncPending = true;
        }

        /// <summary>そのオブジェクトが対象エフェクトの階層に属するか。</summary>
        private bool IsUnderTarget(Object obj)
        {
            Transform transform = obj switch
            {
                GameObject gameObject => gameObject.transform,
                Component component => component.transform,
                _ => null,
            };
            return transform != null && transform.IsChildOf(root.transform);
        }
    }
}
