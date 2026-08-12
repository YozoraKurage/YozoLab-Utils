using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// PhysBone のギズモ描画が毎回やり直している「ボーン構造の作り直し」を、
    /// 変化が無い間は省く。
    ///
    /// SDK の VRCPhysBoneEditor.Draw は毎フレーム
    ///     script.InitTransforms(!Application.isPlaying)   // エディタでは force = true
    /// を呼ぶ。InitTransforms は force が立っていると早期 return を素通りして、
    ///     bones.Clear();
    ///     transform.root.GetComponentsInChildren&lt;VRCPhysBoneBase&gt;(true);  // 階層全体を走査
    ///     GetTransforms(...);                                             // 全ボーンを作り直す
    /// を毎回やる。PhysBone 1 本ごとにアバター全体を走査するので、本数 × 階層規模で効いてくる。
    ///
    /// 実測（600 ボーン / PhysBone 20 本）では Draw 全体 2.3ms のうち 1.7ms がここだった。
    ///
    /// 結果は入力（階層とコンポーネントの設定）が同じなら毎回同じなので、
    /// 変化した可能性があるときだけ作り直せばよい。ここでは「変化したかもしれない」
    /// 合図を全部拾って、拾えたときに丸ごと捨てる方式にしている。
    /// </summary>
    internal static class PhysBoneInitCache
    {
        // インスタンス ID → 最後に作り直したときの版と時刻
        private static readonly Dictionary<int, (int version, double time)> LastBuild =
            new Dictionary<int, (int, double)>();

        internal static int Hits { get; private set; }
        internal static int Misses { get; private set; }

        /// <summary>
        /// 変化を拾い損ねたときの保険。これを超えたら合図が無くても作り直す。
        /// エディタ上でイベントを出さずに Transform が動く経路（他ツールの
        /// プレビュー等）があっても、ここで必ず追いつく。
        /// </summary>
        internal const double MaxStaleSeconds = 0.5;


        internal static void InvalidateAll()
        {
            LastBuild.Clear();
        }

        internal static void ResetStats()
        {
            Hits = 0;
            Misses = 0;
        }

        /// <summary>
        /// VRCPhysBoneBase.InitTransforms(bool force) の prefix。
        /// false を返すと元の作り直しを飛ばし、前回の bones をそのまま使う。
        /// </summary>
        public static bool InitTransforms_Prefix(object __instance, bool force)
        {
            // ギズモ描画の最中でなければ一切関与しない。
            // 実行時のシミュレーション初期化などはそのまま通す。
            if (!GizmoBatch.IsActive) return true;

            // 再生中は SDK 自身が force = false で呼ぶので、元の早期 return が効く。
            if (Application.isPlaying) return true;

            // アニメーションのプレビュー中は Transform が合図無しで動く。
            if (AnimationMode.InAnimationMode()) return true;

            if (!VRCGizmoAcceleratorSettings.instance.cacheBoneInit) return true;

            // force が立っていない呼び出しは元々 SDK 側が省くので触らない。
            if (!force) return true;

            var component = __instance as Object;
            if (component == null) return true;

            int id = component.GetInstanceID();
            double now = EditorApplication.timeSinceStartup;

            if (LastBuild.TryGetValue(id, out var last)
                && last.version == InvalidationVersion.Current
                && now - last.time < MaxStaleSeconds)
            {
                Hits++;
                return false; // 作り直しを省く
            }

            LastBuild[id] = (InvalidationVersion.Current, now);
            Misses++;
            return true;
        }
    }
}
