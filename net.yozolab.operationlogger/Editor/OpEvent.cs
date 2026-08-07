using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace YozoLab.OperationLogger
{
    /// <summary>
    /// イベント種別(閉じた語彙)。SchemaDoc / _SCHEMA.md と必ず同期させること。
    /// </summary>
    internal static class OpType
    {
        public const string Session = "session";
        public const string Prop = "prop";
        public const string Struct = "struct";
        public const string Sel = "sel";
        public const string UndoRedo = "undo";
        public const string Asset = "asset";
        public const string Scene = "scene";
        public const string Play = "play";
        public const string Win = "win";
        public const string Tool = "tool";
        public const string Compile = "compile";
        public const string Err = "err";
        public const string Cmd = "cmd";
        public const string End = "end";
    }

    /// <summary>
    /// Harmony 捕捉の成否フラグ。セッションヘッダの cap に載せ、
    /// ログを読む側(Claude)が捕捉カバレッジを把握できるようにする。
    /// HarmonyCollectors(YOZOLAB_OPLOG_HARMONY 時のみコンパイル)が設定する。
    /// </summary>
    internal static class OpCaps
    {
        public static bool Available; // Harmony コレクタ自体がコンパイルされているか
        public static bool Menu;      // EditorApplication.ExecuteMenuItem(メニュー/API 経由)
        public static bool Ctx;       // GenericMenu(コンテキストメニュー)
        public static bool Sc;        // ShortcutManagement(ショートカット)
    }

    /// <summary>
    /// 1 行分の JSONL イベントを組み立てるビルダ。
    ///
    /// 目的: トークン効率のため、既定値(null / 空 / n=1 / dur≈0)のキーは出力しない。
    /// JsonUtility はキー省略も順序制御もできないため手書きにしている。
    /// 数値は必ず InvariantCulture で整形すること(ロケールで小数点が変わると壊れる)。
    /// </summary>
    internal sealed class OpEvent
    {
        public string Type { get; private set; }
        private readonly StringBuilder sb = new StringBuilder(160);

        public static OpEvent New(string type) => New(type, DateTime.Now);

        /// <summary>ts にはイベント「開始」時刻を渡す(コアレス済みイベントは初回時刻)。</summary>
        public static OpEvent New(string type, DateTime ts)
        {
            var e = new OpEvent { Type = type };
            e.sb.Append("{\"t\":\"").Append(type).Append("\",\"ts\":\"")
                .Append(ts.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append('"');
            return e;
        }

        /// <summary>null / 空文字はキーごと省略。</summary>
        public OpEvent Str(string key, string value)
        {
            if (string.IsNullOrEmpty(value)) return this;
            sb.Append(",\"").Append(key).Append("\":\"").Append(Escape(value)).Append('"');
            return this;
        }

        public OpEvent Int(string key, long value, long skipIf = long.MinValue)
        {
            if (value == skipIf) return this;
            sb.Append(",\"").Append(key).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        /// <summary>小数 1 桁(dur 秒など向け)。skipIfZero で 0.05 未満を省略。</summary>
        public OpEvent Num(string key, double value, bool skipIfZero = false)
        {
            if (skipIfZero && Math.Abs(value) < 0.05) return this;
            sb.Append(",\"").Append(key).Append("\":").Append(value.ToString("0.#", CultureInfo.InvariantCulture));
            return this;
        }

        public OpEvent Bool(string key, bool value, bool skipIfFalse = false)
        {
            if (skipIfFalse && !value) return this;
            sb.Append(",\"").Append(key).Append("\":").Append(value ? "true" : "false");
            return this;
        }

        /// <summary>値を JSON トークンとしてそのまま埋め込む(呼び出し側が正当性を保証)。</summary>
        public OpEvent Raw(string key, string json)
        {
            if (string.IsNullOrEmpty(json)) return this;
            sb.Append(",\"").Append(key).Append("\":").Append(json);
            return this;
        }

        /// <summary>文字列配列。maxItems 超過分は省略する(総数は別キー n で伝える)。</summary>
        public OpEvent StrArray(string key, IReadOnlyList<string> values, int maxItems = 5)
        {
            if (values == null || values.Count == 0) return this;
            sb.Append(",\"").Append(key).Append("\":[");
            int m = Math.Min(values.Count, maxItems);
            for (int i = 0; i < m; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(Escape(values[i] ?? "")).Append('"');
            }
            sb.Append(']');
            return this;
        }

        /// <summary>閉じ括弧を付けて 1 行の JSON を返す。1 インスタンスにつき 1 回だけ呼ぶこと。</summary>
        public string Finish()
        {
            sb.Append('}');
            return sb.ToString();
        }

        // ------------------------------------------------------------
        // 文字列ユーティリティ
        // ------------------------------------------------------------

        public static string Quote(string s) => "\"" + Escape(s) + "\"";

        public static string Truncate(string s, int max)
            => s == null || s.Length <= max ? s : s.Substring(0, max) + "…";

        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var b = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4"));
                        else b.Append(c);
                        break;
                }
            }
            return b.ToString();
        }
    }
}
