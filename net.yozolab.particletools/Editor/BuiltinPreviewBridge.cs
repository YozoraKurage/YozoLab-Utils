using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace YozoLab.ParticleTools
{
    /// <summary>
    /// Unity 標準のパーティクルプレビューへの内部 API 橋渡し。
    ///
    /// ここが掴んでいるのは UnityEditor.ParticleSystemEditorUtils と
    /// UnityEditor.ParticleSystemEffectUtils で、標準の Particle Effect パネルが
    /// 使っているものと同じ。つまり「時刻を入れて積み直す」も「再生する」も、
    /// 標準パネルとまったく同じ道筋をたどる。
    ///
    /// これが重要なのは <b>サブエミッタ</b> のため。ParticleSystem.Simulate を
    /// C# から呼ぶ方式では、withChildren がサブエミッタの受け側にも直接
    /// Simulate をかけてしまう。受け側は本来「親のイベントで生まれる」システムなので、
    /// 直接叩くと親のイベントで出た粒が消える・二重に歳を取る・自前の Emission が
    /// 余計に走る、という壊れ方をする。ネイティブ側の再シミュレートには
    /// その穴が無い。
    ///
    /// 掴めなかった(将来の改名など)場合は CanDrivePlayback が false になり、
    /// 呼び出し側は Simulate による代替へ落ちる。
    /// </summary>
    internal static class BuiltinPreviewBridge
    {
        private const BindingFlags StaticMember =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly PropertyInfo playbackTime;
        private static readonly PropertyInfo playbackIsPlaying;
        private static readonly PropertyInfo playbackIsPaused;
        private static readonly PropertyInfo playbackIsScrubbing;
        private static readonly PropertyInfo simulationSpeed;
        private static readonly PropertyInfo resimulation;
        private static readonly PropertyInfo previewLayers;
        private static readonly PropertyInfo renderInSceneView;
        private static readonly MethodInfo performCompleteResimulation;
        private static readonly MethodInfo stopEffect;

        /// <summary>掴めなかったものの一覧。掴めていれば見つけたアセンブリ名。</summary>
        public static string Diagnostics { get; private set; } = "";

        static BuiltinPreviewBridge()
        {
            try
            {
                Type utils = FindEditorType("UnityEditor.ParticleSystemEditorUtils");
                Type effectUtils = FindEditorType("UnityEditor.ParticleSystemEffectUtils");

                // 2019.3 以降は playback*/simulationSpeed、それ以前は editor* という名前。
                playbackTime = Find(utils, "playbackTime", "editorPlaybackTime");
                playbackIsPlaying = Find(utils, "playbackIsPlaying", "editorIsPlaying");
                playbackIsPaused = Find(utils, "playbackIsPaused", "editorIsPaused");
                playbackIsScrubbing = Find(utils, "playbackIsScrubbing", "editorIsScrubbing");
                simulationSpeed = Find(utils, "simulationSpeed", "editorSimulationSpeed");
                resimulation = Find(utils, "resimulation", "editorResimulation");
                previewLayers = Find(utils, "previewLayers", "editorPreviewLayers");
                renderInSceneView = Find(utils, "renderInSceneView", "editorRenderInSceneView");

                performCompleteResimulation =
                    utils?.GetMethod("PerformCompleteResimulation", StaticMember, null, Type.EmptyTypes, null);
                stopEffect =
                    effectUtils?.GetMethod("StopEffect", StaticMember, null, Type.EmptyTypes, null);

                Diagnostics = BuildDiagnostics(utils);
            }
            catch (Exception e)
            {
                // 何も掴めなくても機能全体は生かす。
                Diagnostics = e.Message;
            }
        }

        /// <summary>
        /// 型を名前で引く。UnityEditor.Editor と同じアセンブリにあるとは限らない
        /// (エディタは UnityEditor.CoreModule と UnityEditor.*Module に分かれていて、
        /// どのモジュールへ入るかは Unity のバージョンで変わる)ので、
        /// 見つからなければ読み込み済みアセンブリを順に当たる。
        /// </summary>
        internal static Type FindEditorType(string fullName)
        {
            Type type = typeof(Editor).Assembly.GetType(fullName);
            if (type != null) return type;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetType(fullName);
                    if (type != null) return type;
                }
                catch
                {
                    // 読めないアセンブリは飛ばす。
                }
            }
            return null;
        }

        private static PropertyInfo Find(Type type, string name, string legacyName)
        {
            if (type == null) return null;
            return type.GetProperty(name, StaticMember) ?? type.GetProperty(legacyName, StaticMember);
        }

        /// <summary>何が掴めて何が掴めなかったかを 1 行にまとめる。表示と調査用。</summary>
        private static string BuildDiagnostics(Type utils)
        {
            if (utils == null) return "UnityEditor.ParticleSystemEditorUtils が見つからない";

            var missing = new List<string>();
            if (playbackTime == null) missing.Add("playbackTime");
            if (playbackIsScrubbing == null) missing.Add("playbackIsScrubbing");
            if (simulationSpeed == null) missing.Add("simulationSpeed");
            if (resimulation == null) missing.Add("resimulation");
            if (performCompleteResimulation == null) missing.Add("PerformCompleteResimulation()");
            if (stopEffect == null) missing.Add("ParticleSystemEffectUtils.StopEffect()");

            return missing.Count == 0
                ? utils.Assembly.GetName().Name
                : $"{utils.Assembly.GetName().Name} に {string.Join(", ", missing)} が無い";
        }

        // ---------------------------------------------------------------
        // 掴めたかどうか
        // ---------------------------------------------------------------

        /// <summary>
        /// ネイティブ側の再生・再シミュレートを操作できるか。
        /// false のときはサブエミッタが正しく出ない代替経路になる。
        /// </summary>
        public static bool CanDrivePlayback =>
            playbackTime != null && performCompleteResimulation != null;

        public static bool HasPreviewLayers => previewLayers != null;

        public static bool HasShowOnlySelected => renderInSceneView != null;

        // ---------------------------------------------------------------
        // 再生状態
        // ---------------------------------------------------------------

        /// <summary>標準パネルの Playback Time。書いてから Resimulate() でその時刻の絵になる。</summary>
        public static float PlaybackTime
        {
            get => GetFloat(playbackTime, 0f);
            set => Set(playbackTime, value);
        }

        public static bool IsPlaying
        {
            get => GetBool(playbackIsPlaying, false);
            set => Set(playbackIsPlaying, value);
        }

        public static bool IsPaused
        {
            get => GetBool(playbackIsPaused, false);
            set => Set(playbackIsPaused, value);
        }

        /// <summary>
        /// 標準側の「今スクラブ中」フラグ。これが落ちていると停止扱いになり、
        /// 再シミュレートの要求がネイティブ側で捨てられる。
        /// </summary>
        public static bool IsScrubbing
        {
            get => GetBool(playbackIsScrubbing, false);
            set => Set(playbackIsScrubbing, value);
        }

        /// <summary>標準パネルの Playback Speed。</summary>
        public static float SimulationSpeed
        {
            get => GetFloat(simulationSpeed, 1f);
            set => Set(simulationSpeed, value);
        }

        /// <summary>
        /// 標準パネルの Resimulate。ON だと ParticleSystemUI.ApplyProperties が
        /// 変更のあった GUI イベントごとに先頭からの積み直しを走らせる。
        /// </summary>
        public static bool Resimulation
        {
            get => GetBool(resimulation, true);
            set => Set(resimulation, value);
        }

        /// <summary>プレビューをシミュレートするレイヤーのビットマスク。標準パネルの Simulate Layers。</summary>
        public static uint PreviewLayers
        {
            get
            {
                try { return (uint)previewLayers.GetValue(null); }
                catch { return uint.MaxValue; }
            }
            set
            {
                try { previewLayers?.SetValue(null, value); }
                catch { }
            }
        }

        /// <summary>
        /// 選択中のエフェクト以外のパーティクルを Scene ビューで隠す。標準パネルの
        /// Show Only Selected。内部プロパティ renderInSceneView の裏返しとして扱う。
        /// </summary>
        public static bool ShowOnlySelected
        {
            get => !GetBool(renderInSceneView, true);
            set => Set(renderInSceneView, !value);
        }

        // ---------------------------------------------------------------
        // 操作
        // ---------------------------------------------------------------

        /// <summary>PlaybackTime の時刻へ、ネイティブ側で先頭から積み直す。</summary>
        public static void Resimulate()
        {
            try { performCompleteResimulation?.Invoke(null, null); }
            catch { }
        }

        /// <summary>標準パネルの Stop 相当。動いているエフェクトを止めて粒を消す。</summary>
        public static void StopEffect()
        {
            try { stopEffect?.Invoke(null, null); }
            catch { }
        }

        /// <summary>
        /// 標準側の再生を止める。ネイティブ駆動が使えず Simulate で代替している
        /// ときだけ必要になる(同じエフェクトを毎フレーム取り合うため)。
        /// </summary>
        public static void PausePlayback()
        {
            if (playbackIsPlaying == null) return;
            try
            {
                if ((bool)playbackIsPlaying.GetValue(null))
                    playbackIsPlaying.SetValue(null, false);
            }
            catch
            {
                // 内部 API が変わっただけなら巻き込まない。
            }
        }

        // ---------------------------------------------------------------
        // 借りている間の退避と返却
        // ---------------------------------------------------------------

        private static bool owned;
        private static bool savedResimulation;
        private static float savedSimulationSpeed;

        /// <summary>
        /// エフェクトを掴んだときに一度だけ呼ぶ。標準の自動再シミュレートを切る。
        ///
        /// これを切るのが、Inspector を触るとシーンがカクつく件の本丸。ON のままだと
        /// ParticleSystemUI.ApplyProperties が「変更のあった GUI イベントごと」に
        /// 先頭からの積み直しを要求する。スライダを 1 秒ドラッグすれば数十回、
        /// しかも 1 回あたり 0 秒目から現在時刻までの全ステップなので、サブエミッタを
        /// 含むエフェクトでは ParticleSystem.Update がフレーム時間を食い切る。
        /// 代わりに、こちら側で間引いた再シミュレートを掛け直す。
        /// </summary>
        public static void BeginOwnership()
        {
            if (owned) return;

            savedResimulation = Resimulation;
            savedSimulationSpeed = SimulationSpeed;
            owned = true;

            Resimulation = false;
        }

        /// <summary>手を放すときに呼ぶ。退避しておいた標準側の設定を戻す。</summary>
        public static void EndOwnership()
        {
            if (!owned) return;
            owned = false;

            Resimulation = savedResimulation;
            SimulationSpeed = savedSimulationSpeed;
            IsScrubbing = false;
        }

        // ---------------------------------------------------------------

        private static bool GetBool(PropertyInfo property, bool fallback)
        {
            if (property == null) return fallback;
            try { return (bool)property.GetValue(null); }
            catch { return fallback; }
        }

        private static float GetFloat(PropertyInfo property, float fallback)
        {
            if (property == null) return fallback;
            try { return (float)property.GetValue(null); }
            catch { return fallback; }
        }

        private static void Set(PropertyInfo property, object value)
        {
            if (property == null) return;
            try { property.SetValue(null, value); }
            catch { }
        }
    }
}
