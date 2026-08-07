using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace YozoLab.OperationLogger
{
    /// <summary>
    /// ライフサイクル中枢。
    ///
    /// 目的: ユーザーの Unity 操作を Claude 可読な JSONL に記録し、
    ///       後から AI がフリクション(詰まり)を分析できるようにする。
    /// 実装: セッションの開始/終了/リロード跨ぎ継続、コレクタの購読管理、
    ///       update tick でのフラッシュ駆動を担う。イベントの意味づけは
    ///       Normalizer/Coalescer、書き出しは JsonlSink に委譲する。
    /// </summary>
    [InitializeOnLoad]
    internal static class OpLogger
    {
        private const string PrefEnabled = "YozoLab.OperationLogger.Enabled";

        // SessionState キー(エディタ再起動でクリア、ドメインリロードは生存)
        internal const string StateFile = "YozoLab.OpLog.File";
        internal const string StateCounts = "YozoLab.OpLog.Counts";
        internal const string StateBytes = "YozoLab.OpLog.Bytes";
        internal const string StatePlayStart = "YozoLab.OpLog.PlayStart";
        internal const string StateCompileStart = "YozoLab.OpLog.CompileStart";
        private const string StateSessionStart = "YozoLab.OpLog.SessionStart";
        private const string StateReloadStart = "YozoLab.OpLog.ReloadStart";

        private static bool sessionActive;
        private static DateTime sessionStart;

        /// <summary>ユーザー毎のマスタースイッチ(既定 OFF)。</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefEnabled, false);
            private set => EditorPrefs.SetBool(PrefEnabled, value);
        }

        /// <summary>セッションが動いているか(play/err などの環境イベントはこれだけで記録)。</summary>
        public static bool IsRecording => sessionActive;

        /// <summary>編集系イベント(prop/struct/sel/win/tool/cmd)を今記録すべきか。プレイ中は既定で停止。</summary>
        public static bool EditsAllowed => sessionActive
            && (OpLoggerSettings.instance.playModeCapture || !EditorApplication.isPlayingOrWillChangePlaymode);

        static OpLogger()
        {
            // ScriptableSingleton の読込を伴う初期化は delayCall まで遅らせる
            EditorApplication.delayCall += Init;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            EditorApplication.quitting += OnQuitting;
        }

        private static void Init()
        {
            if (Enabled) StartOrContinue();
        }

        // ------------------------------------------------------------
        // セッション制御
        // ------------------------------------------------------------

        public static void SetEnabled(bool on)
        {
            if (on == Enabled) return;
            Enabled = on;
            if (on)
            {
                StartOrContinue(forceNew: true);
                Debug.Log("[OpLogger] recording started → " + JsonlSink.CurrentFile);
            }
            else
            {
                EndSession(writeEnd: true);
                Debug.Log("[OpLogger] recording stopped");
            }
        }

        public static void RestartSession()
        {
            EndSession(writeEnd: true);
            StartOrContinue(forceNew: true);
            Debug.Log("[OpLogger] new session → " + JsonlSink.CurrentFile);
        }

        private static void StartOrContinue(bool forceNew = false)
        {
            if (sessionActive) return;

            string prev = SessionState.GetString(StateFile, "");
            if (!forceNew && prev.Length > 0 && File.Exists(prev))
            {
                // ドメインリロード明け: 同一ファイルへ継続
                JsonlSink.Continue(prev);
                sessionStart = long.TryParse(SessionState.GetString(StateSessionStart, ""), out long ticks)
                    ? new DateTime(ticks) : DateTime.Now;
            }
            else
            {
                JsonlSink.StartNew();
                sessionStart = DateTime.Now;
                SessionState.SetString(StateSessionStart,
                    sessionStart.Ticks.ToString(CultureInfo.InvariantCulture));
                SessionState.SetString(StateFile, JsonlSink.CurrentFile);
            }

            sessionActive = true;
            CoreCollectors.Subscribe();
            EnvCollectors.Subscribe();
            EditorApplication.update += OnUpdate;
            EmitPendingReloadDur();
        }

        private static void EndSession(bool writeEnd)
        {
            if (!sessionActive) return;
            Coalescer.FlushAll();
            if (writeEnd) JsonlSink.WriteEnd((DateTime.Now - sessionStart).TotalSeconds);
            JsonlSink.Flush();
            JsonlSink.Reset();
            SessionState.EraseString(StateSessionStart);
            CoreCollectors.Unsubscribe();
            EnvCollectors.Unsubscribe();
            EditorApplication.update -= OnUpdate;
            sessionActive = false;
        }

        // ------------------------------------------------------------
        // ドメインリロード / 終了
        // ------------------------------------------------------------

        private static void OnBeforeReload()
        {
            if (!sessionActive) return;
            // セッション中のみ退避する(OFF 中に退避すると、後で ON にしたとき
            // 偽の巨大な reload dur が出てしまう)
            SessionState.SetFloat(StateReloadStart, (float)EditorApplication.timeSinceStartup);
            Coalescer.FlushAll();
            JsonlSink.Flush();
            JsonlSink.PersistState();
        }

        /// <summary>リロード直後: 前ドメインで記録した開始時刻からリロード所要を算出。</summary>
        private static void EmitPendingReloadDur()
        {
            float start = SessionState.GetFloat(StateReloadStart, -1f);
            if (start < 0f) return;
            SessionState.EraseFloat(StateReloadStart);
            // Play 遷移に伴うリロードはコンパイル待ちではないため除外
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            double dur = EditorApplication.timeSinceStartup - start;
            if (dur > 0.05)
                Emit(OpEvent.New(OpType.Compile).Str("op", "reload").Num("dur", dur));
        }

        private static void OnQuitting()
        {
            EndSession(writeEnd: true);
        }

        // ------------------------------------------------------------
        // tick / 出力
        // ------------------------------------------------------------

        private static void OnUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            Coalescer.Tick(now);
            CoreCollectors.Tick(now);
            EnvCollectors.Tick(now);
            JsonlSink.Tick(now);
        }

        public static void Emit(OpEvent e)
        {
            if (!sessionActive) return;
            JsonlSink.Append(e);
        }

        /// <summary>pending をすべて確定してディスクへ書く(play 遷移などの区切りで使用)。</summary>
        public static void FlushAll()
        {
            if (!sessionActive) return;
            Coalescer.FlushAll();
            JsonlSink.Flush();
        }
    }
}
