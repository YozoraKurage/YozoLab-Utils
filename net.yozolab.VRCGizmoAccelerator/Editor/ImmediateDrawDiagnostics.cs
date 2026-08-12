using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// 「即時描画が何回発行されているか、誰が出しているか」を数える診断。
    ///
    /// シーンビューの IMGUI が重いとき、原因は頂点数ではなく発行回数であることが多い。
    /// UIToolkit のレンダラは即時描画が挟まるたびに溜めていたものを吐き出して
    /// レンダーターゲットを貼り直すため、細切れに出すほど高くつく。
    ///
    /// 発行のたびに呼ばれる HandleUtility.ApplyWireMaterial（= SetPass）を数えれば、
    /// その回数がそのまま分断の回数になる。呼び出し元も控えるので、
    /// このツールの担当（VRChat SDK）以外に犯人がいるかどうかが分かる。
    ///
    /// スタックトレースは高いので、リセットごとに決まった数だけ拾う。
    /// </summary>
    internal static class ImmediateDrawDiagnostics
    {
        private const int MaxSamples = 32;

        internal static bool Enabled { get; private set; }

        internal static int Calls { get; private set; }
        internal static int CallsInsideOurScope { get; private set; }

        private static readonly Dictionary<string, int> Callers = new Dictionary<string, int>();
        private static int _sampled;
        private static double _startedAt;

        internal static IEnumerable<KeyValuePair<string, int>> TopCallers =>
            Callers.OrderByDescending(p => p.Value).Take(8);

        internal static double ElapsedSeconds =>
            Math.Max(0.001, EditorApplication.timeSinceStartup - _startedAt);

        internal static void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            Reset();
        }

        internal static void Reset()
        {
            Calls = 0;
            CallsInsideOurScope = 0;
            _sampled = 0;
            Callers.Clear();
            _startedAt = EditorApplication.timeSinceStartup;
        }

        /// <summary>
        /// HandleUtility.ApplyWireMaterial の prefix。数えるだけで、
        /// 常に true を返して元の実装をそのまま走らせる。
        /// </summary>
        public static bool ApplyWireMaterial_Prefix()
        {
            if (!Enabled) return true;

            Calls++;

            // 自分が流している最中（区間を閉じたあと）も「担当内」に数える。
            // ここを取りこぼすと、自分の発行を他人のものとして数えてしまう。
            if (GizmoBatch.IsActive || GizmoBatch.Flushing) CallsInsideOurScope++;

            if (_sampled < MaxSamples)
            {
                _sampled++;
                Record();
            }

            return true;
        }

        private static void Record()
        {
            try
            {
                // ファイル情報は要らないので取らない（そのぶん安い）
                var trace = new StackTrace(2, false);
                for (int i = 0; i < trace.FrameCount; i++)
                {
                    var method = trace.GetFrame(i)?.GetMethod();
                    var type = method?.DeclaringType;
                    if (type == null) continue;

                    // 自分と Harmony の中間フレームは飛ばす
                    var asmName = type.Assembly.GetName().Name;
                    if (asmName.StartsWith("net.yozolab") || asmName.StartsWith("0Harmony")) continue;

                    var key = $"{asmName} : {type.Name}.{method.Name}";
                    Callers.TryGetValue(key, out int count);
                    Callers[key] = count + 1;
                    return;
                }
            }
            catch (Exception)
            {
                // 診断で例外を出して本体を止めるのは本末転倒なので握り潰す
            }
        }

        internal static MethodInfo Prefix =>
            typeof(ImmediateDrawDiagnostics).GetMethod(
                "ApplyWireMaterial_Prefix", BindingFlags.Static | BindingFlags.Public);

        internal static MethodBase Target
        {
            get
            {
                return typeof(HandleUtility).GetMethod(
                    "ApplyWireMaterial",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(UnityEngine.Rendering.CompareFunction) },
                    null);
            }
        }
    }
}
