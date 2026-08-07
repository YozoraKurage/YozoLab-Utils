using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace YozoLab.OperationLogger
{
    /// <summary>
    /// 環境系コレクタ(シーン/プレハブステージ/プレイモード/ウィンドウ/ツール/コンパイル/エラー)。
    /// play のイテレーション周期・compile 待ち時間・エラーバーストはフリクション検出の本命シグナル。
    /// </summary>
    internal static class EnvCollectors
    {
        private static bool subscribed;

        // ウィンドウフォーカス(ポーリング)
        private static string curWin;
        private static double winStart;
        private static double lastWinPoll;

        // ツール
        private static string lastTool;

        // エラー集約(同一メッセージのバーストを 1 行に)
        private sealed class PendingErr
        {
            public string kind, msg;
            public int n;
            public double lastT;
            public DateTime firstWall;
            public double firstT;
        }
        private static readonly Dictionary<string, PendingErr> errs = new Dictionary<string, PendingErr>();
        private static int errDropped;

        public static void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            PrefabStage.prefabStageOpened += OnPrefabOpened;
            PrefabStage.prefabStageClosing += OnPrefabClosing;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            ToolManager.activeToolChanged += OnToolChanged;
            CompilationPipeline.compilationStarted += OnCompileStarted;
            CompilationPipeline.compilationFinished += OnCompileFinished;
            Application.logMessageReceived += OnLogMessage;
        }

        public static void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed = false;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            PrefabStage.prefabStageOpened -= OnPrefabOpened;
            PrefabStage.prefabStageClosing -= OnPrefabClosing;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            ToolManager.activeToolChanged -= OnToolChanged;
            CompilationPipeline.compilationStarted -= OnCompileStarted;
            CompilationPipeline.compilationFinished -= OnCompileFinished;
            Application.logMessageReceived -= OnLogMessage;
        }

        public static void Tick(double now)
        {
            PollWindowFocus(now);
            FlushIdleErrors(now);
        }

        // ------------------------------------------------------------
        // シーン / プレハブステージ
        // ------------------------------------------------------------

        private static void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
        {
            try
            {
                Normalizer.ClearCache(); // 旧シーンのパスキャッシュは無効
                if (!OpLogger.IsRecording) return;
                OpLogger.Emit(OpEvent.New(OpType.Scene).Str("op", "open").Str("path", scene.path));
            }
            catch { }
        }

        private static void OnSceneSaved(UnityEngine.SceneManagement.Scene scene)
        {
            try
            {
                if (!OpLogger.IsRecording) return;
                Coalescer.FlushAll(); // 保存 = 区切りとして pending を確定
                OpLogger.Emit(OpEvent.New(OpType.Scene).Str("op", "save").Str("path", scene.path));
            }
            catch { }
        }

        private static void OnPrefabOpened(PrefabStage stage)
        {
            try
            {
                if (!OpLogger.IsRecording) return;
                OpLogger.Emit(OpEvent.New(OpType.Scene).Str("op", "prefab_open").Str("path", stage.assetPath));
            }
            catch { }
        }

        private static void OnPrefabClosing(PrefabStage stage)
        {
            try
            {
                if (!OpLogger.IsRecording) return;
                Coalescer.FlushEdits(); // ステージ内パスが無効になる前に確定
                OpLogger.Emit(OpEvent.New(OpType.Scene).Str("op", "prefab_close").Str("path", stage.assetPath));
            }
            catch { }
        }

        // ------------------------------------------------------------
        // プレイモード(イテレーションループ検出の本命)
        // ------------------------------------------------------------

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            try
            {
                switch (state)
                {
                    case PlayModeStateChange.ExitingEditMode:
                        // ドメインリロード無効設定でも取りこぼさないようここで確定
                        OpLogger.FlushAll();
                        break;
                    case PlayModeStateChange.EnteredPlayMode:
                        if (!OpLogger.IsRecording) return;
                        SessionState.SetFloat(OpLogger.StatePlayStart, (float)EditorApplication.timeSinceStartup);
                        OpLogger.Emit(OpEvent.New(OpType.Play).Str("op", "enter"));
                        break;
                    case PlayModeStateChange.EnteredEditMode:
                        if (!OpLogger.IsRecording) return;
                        float start = SessionState.GetFloat(OpLogger.StatePlayStart, -1f);
                        SessionState.EraseFloat(OpLogger.StatePlayStart);
                        var e = OpEvent.New(OpType.Play).Str("op", "exit");
                        if (start >= 0f) e.Num("dur", EditorApplication.timeSinceStartup - start);
                        OpLogger.Emit(e);
                        break;
                }
            }
            catch { }
        }

        // ------------------------------------------------------------
        // ウィンドウフォーカス / アクティブツール
        // ------------------------------------------------------------

        private static void PollWindowFocus(double now)
        {
            if (now - lastWinPoll < 0.25) return;
            lastWinPoll = now;
            try
            {
                if (!OpLogger.EditsAllowed || !OpLoggerSettings.instance.logWindowFocus) return;
                var fw = EditorWindow.focusedWindow;
                string name = fw != null ? fw.GetType().Name : null;
                if (name == null || name == curWin) return; // null = アプリ非フォーカス。遷移とはみなさない

                var e = OpEvent.New(OpType.Win).Str("win", name).Str("prev", curWin);
                if (curWin != null) e.Num("prevDur", now - winStart, skipIfZero: true);
                OpLogger.Emit(e);
                curWin = name;
                winStart = now;
            }
            catch { }
        }

        private static void OnToolChanged()
        {
            try
            {
                if (!OpLogger.EditsAllowed) return;
                string name = Tools.current == Tool.Custom
                    ? (ToolManager.activeToolType != null ? ToolManager.activeToolType.Name : "Custom")
                    : Tools.current.ToString();
                if (name == lastTool) return;
                lastTool = name;
                OpLogger.Emit(OpEvent.New(OpType.Tool).Str("tool", name));
            }
            catch { }
        }

        // ------------------------------------------------------------
        // コンパイル(待ち時間 = フリクション)
        // ------------------------------------------------------------

        private static void OnCompileStarted(object ctx)
        {
            SessionState.SetFloat(OpLogger.StateCompileStart, (float)EditorApplication.timeSinceStartup);
        }

        private static void OnCompileFinished(object ctx)
        {
            try
            {
                if (!OpLogger.IsRecording) return;
                float start = SessionState.GetFloat(OpLogger.StateCompileStart, -1f);
                SessionState.EraseFloat(OpLogger.StateCompileStart);
                if (start < 0f) return;
                OpLogger.Emit(OpEvent.New(OpType.Compile).Str("op", "end")
                    .Num("dur", EditorApplication.timeSinceStartup - start));
            }
            catch { }
        }

        // ------------------------------------------------------------
        // エラー / 例外(バースト = 詰まりシグナル)
        // ------------------------------------------------------------

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            // 重要: このハンドラ内から Debug.Log 系を呼ばないこと(再帰する)
            try
            {
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
                if (!OpLogger.IsRecording || !OpLoggerSettings.instance.logErrors) return;
                if (condition != null && condition.Contains("[OpLogger]")) return; // 自己再帰防止

                string msg = OpEvent.Truncate(condition ?? "", 160);
                double now = EditorApplication.timeSinceStartup;
                if (errs.TryGetValue(msg, out var p))
                {
                    p.n++;
                    p.lastT = now;
                    return;
                }
                if (errs.Count >= 10) { errDropped++; return; } // rate-limit
                errs[msg] = new PendingErr
                {
                    kind = type == LogType.Exception ? "exception" : "error",
                    msg = msg, n = 1, firstT = now, lastT = now, firstWall = DateTime.Now,
                };
            }
            catch { }
        }

        private static void FlushIdleErrors(double now)
        {
            if (errs.Count == 0)
            {
                if (errDropped > 0)
                {
                    int d = errDropped;
                    errDropped = 0;
                    OpLogger.Emit(OpEvent.New(OpType.Err).Str("kind", "note")
                        .Str("msg", "(rate-limited: " + d + " more distinct errors)"));
                }
                return;
            }
            List<string> due = null;
            foreach (var kv in errs)
                if (now - kv.Value.lastT > 5.0) (due ??= new List<string>()).Add(kv.Key);
            if (due == null) return;
            foreach (var k in due)
            {
                var p = errs[k];
                errs.Remove(k);
                OpLogger.Emit(OpEvent.New(OpType.Err, p.firstWall)
                    .Str("kind", p.kind).Str("msg", p.msg)
                    .Int("n", p.n, skipIf: 1)
                    .Num("dur", p.lastT - p.firstT, skipIfZero: true));
            }
        }
    }

    /// <summary>アセットのインポート/削除/移動。ストーム(パッケージ更新等)は拡張子ヒストグラム 1 行に畳む。</summary>
    internal sealed class OpAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            try
            {
                if (!OpLogger.IsRecording || !OpLoggerSettings.instance.logAssets) return;
                EmitBatch("import", importedAssets);
                EmitBatch("delete", deletedAssets);
                EmitMoves(movedAssets, movedFromAssetPaths);
            }
            catch { }
        }

        internal static bool Ignored(string path)
            => string.IsNullOrEmpty(path)
               || path.StartsWith("Logs/", StringComparison.Ordinal)
               || path.StartsWith("Library/", StringComparison.Ordinal)
               || path.StartsWith("ProjectSettings/", StringComparison.Ordinal);

        private static void EmitBatch(string op, string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            var list = paths.Where(p => !Ignored(p)).ToList();
            if (list.Count == 0) return;

            if (list.Count > 10)
            {
                // ストーム: 拡張子ヒストグラムに畳む
                var hist = list.GroupBy(p => Path.GetExtension(p).ToLowerInvariant())
                    .OrderByDescending(g => g.Count());
                var sb = new StringBuilder("{");
                bool first = true;
                foreach (var g in hist)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"').Append(OpEvent.Escape(g.Key.Length == 0 ? "(none)" : g.Key))
                        .Append("\":").Append(g.Count());
                }
                sb.Append('}');
                OpLogger.Emit(OpEvent.New(OpType.Asset).Str("op", op)
                    .Int("n", list.Count).Raw("ext", sb.ToString()).Str("note", "bulk"));
            }
            else
            {
                OpLogger.Emit(OpEvent.New(OpType.Asset).Str("op", op)
                    .StrArray("paths", list, 10).Int("n", list.Count, skipIf: 1));
            }
        }

        private static void EmitMoves(string[] to, string[] from)
        {
            if (to == null || to.Length == 0) return;
            var pairs = new List<string>();
            for (int i = 0; i < to.Length && i < from.Length; i++)
                if (!Ignored(to[i])) pairs.Add(from[i] + " -> " + to[i]);
            if (pairs.Count == 0) return;
            OpLogger.Emit(OpEvent.New(OpType.Asset).Str("op", "move")
                .StrArray("paths", pairs, 10).Int("n", pairs.Count, skipIf: 1));
        }
    }

    /// <summary>アセット保存の捕捉。OnWillSaveAssets は受け取った配列を必ずそのまま返すこと。</summary>
    internal sealed class OpAssetModProcessor : UnityEditor.AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            try
            {
                if (OpLogger.IsRecording && OpLoggerSettings.instance.logAssets
                    && paths != null && paths.Length > 0)
                {
                    var list = paths.Where(p => !OpAssetPostprocessor.Ignored(p)).ToList();
                    if (list.Count > 0)
                        OpLogger.Emit(OpEvent.New(OpType.Asset).Str("op", "save")
                            .StrArray("paths", list, 10).Int("n", list.Count, skipIf: 1));
                }
            }
            catch { }
            return paths;
        }
    }
}
