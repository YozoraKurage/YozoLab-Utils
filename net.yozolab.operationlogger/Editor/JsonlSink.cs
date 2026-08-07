using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YozoLab.OperationLogger
{
    /// <summary>
    /// JSONL 書き出し層。
    ///
    /// ・出力先は Assets 外の Logs/(自分の書き込みでインポート→再記録の無限ループを防ぐ)
    /// ・StreamWriter を保持せず flush 毎に open-append-close(リロード/クラッシュ耐性)
    /// ・ドメインリロードを跨いでも SessionState 経由で同一ファイルへ追記継続する
    /// </summary>
    internal static class JsonlSink
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static string filePath;
        private static readonly List<string> buffer = new List<string>();
        private static readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        private static long bytesWritten;
        private static double lastFlushT;

        public static bool Active => filePath != null;
        public static string CurrentFile => filePath;

        public static string LogDirectory
            => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "YozoLab", "oplog");

        // ------------------------------------------------------------
        // セッション開始 / 継続
        // ------------------------------------------------------------

        public static void StartNew()
        {
            string dir = LogDirectory;
            Directory.CreateDirectory(dir);
            string name = DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture)
                + "_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".jsonl";
            filePath = Path.Combine(dir, name);
            buffer.Clear();
            counts.Clear();
            bytesWritten = 0;

            // ログの読み方(スキーマ+分析ガイド)を同じフォルダに置く。
            // どのセッションの Claude でも自己記述的に読めるようにするため。
            try { File.WriteAllText(Path.Combine(dir, "_SCHEMA.md"), SchemaDoc.Markdown, Utf8NoBom); }
            catch { }

            AppendLine(OpType.Session, BuildHeader(cont: false));
            Flush(); // ヘッダは即書き(ファイルの存在を保証)
        }

        /// <summary>ドメインリロード後、SessionState に退避した状態で同一ファイルへ継続する。</summary>
        public static void Continue(string path)
        {
            filePath = path;
            buffer.Clear();
            counts.Clear();
            bytesWritten = 0;

            string csv = SessionState.GetString(OpLogger.StateCounts, "");
            foreach (string pair in csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0 && int.TryParse(pair.Substring(eq + 1), out int v))
                    counts[pair.Substring(0, eq)] = v;
            }
            if (long.TryParse(SessionState.GetString(OpLogger.StateBytes, ""), out long b))
                bytesWritten = b;
        }

        // ------------------------------------------------------------
        // 書き込み
        // ------------------------------------------------------------

        public static void Append(OpEvent e)
        {
            if (filePath == null) return;
            AppendLine(e.Type, e.Finish());
            if (buffer.Count >= 50) Flush();
        }

        private static void AppendLine(string type, string line)
        {
            counts.TryGetValue(type, out int c);
            counts[type] = c + 1;
            buffer.Add(line + "\n");
        }

        public static void Tick(double now)
        {
            if (buffer.Count > 0 && now - lastFlushT > 2.0) Flush();
        }

        public static void Flush()
        {
            if (filePath == null || buffer.Count == 0) return;
            var sb = new StringBuilder();
            foreach (string l in buffer) sb.Append(l);
            buffer.Clear();
            string chunk = sb.ToString();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                File.AppendAllText(filePath, chunk, Utf8NoBom);
                bytesWritten += Utf8NoBom.GetByteCount(chunk);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[OpLogger] ログ書込に失敗: " + ex.Message);
            }
            lastFlushT = EditorApplication.timeSinceStartup;

            long cap = (long)OpLoggerSettings.instance.rotationMB * 1024 * 1024;
            if (bytesWritten > cap) Rotate();
        }

        /// <summary>サイズ上限でファイルを切り替え、継続ヘッダを書く。</summary>
        private static void Rotate()
        {
            string dir = Path.GetDirectoryName(filePath);
            string stem = Path.GetFileNameWithoutExtension(filePath);
            int part = 2;
            int idx = stem.LastIndexOf("_part", StringComparison.Ordinal);
            if (idx >= 0 && int.TryParse(stem.Substring(idx + 5), out int cur))
            {
                part = cur + 1;
                stem = stem.Substring(0, idx);
            }
            filePath = Path.Combine(dir, stem + "_part" + part + ".jsonl");
            bytesWritten = 0;
            try { File.AppendAllText(filePath, BuildHeader(cont: true) + "\n", Utf8NoBom); }
            catch { }
            SessionState.SetString(OpLogger.StateFile, filePath);
        }

        // ------------------------------------------------------------
        // ヘッダ / 終端 / 状態退避
        // ------------------------------------------------------------

        private static string BuildHeader(bool cont)
        {
            string capJson = "{\"menu\":" + (OpCaps.Menu ? "true" : "false")
                + ",\"ctx\":" + (OpCaps.Ctx ? "true" : "false")
                + ",\"sc\":" + (OpCaps.Sc ? "true" : "false") + "}";
            return OpEvent.New(OpType.Session)
                .Str("date", DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Str("unity", Application.unityVersion)
                .Str("proj", new DirectoryInfo(Path.GetDirectoryName(Application.dataPath)).Name)
                .Str("scene", SceneManager.GetActiveScene().path)
                .Str("schema", "v1")
                .Raw("cap", capJson)
                .Bool("cont", cont, skipIfFalse: true)
                .Finish();
        }

        public static void WriteEnd(double sessionDurSec)
        {
            if (filePath == null) return;
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var kv in counts)
            {
                if (kv.Key == OpType.Session) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(kv.Key).Append("\":").Append(kv.Value);
            }
            sb.Append('}');
            buffer.Add(OpEvent.New(OpType.End)
                .Num("dur", sessionDurSec)
                .Raw("counts", sb.ToString())
                .Finish() + "\n");
            Flush();
        }

        /// <summary>ドメインリロード前に呼ぶ。継続に必要な状態を SessionState へ退避する。</summary>
        public static void PersistState()
        {
            if (filePath == null) return;
            SessionState.SetString(OpLogger.StateFile, filePath);
            var sb = new StringBuilder();
            foreach (var kv in counts)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(kv.Key).Append('=').Append(kv.Value);
            }
            SessionState.SetString(OpLogger.StateCounts, sb.ToString());
            SessionState.SetString(OpLogger.StateBytes, bytesWritten.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>セッション終了。退避状態も破棄する。</summary>
        public static void Reset()
        {
            filePath = null;
            buffer.Clear();
            counts.Clear();
            bytesWritten = 0;
            SessionState.EraseString(OpLogger.StateFile);
            SessionState.EraseString(OpLogger.StateCounts);
            SessionState.EraseString(OpLogger.StateBytes);
        }
    }
}
