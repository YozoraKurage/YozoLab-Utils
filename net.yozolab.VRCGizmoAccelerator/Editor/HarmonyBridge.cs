using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// Harmony への薄いブリッジ。全て Reflection 経由で触る。
    ///
    /// Harmony は VRChat SDK (com.vrchat.base) が同梱している 0Harmony.dll を使う。
    /// asmdef から参照してしまうと SDK の無いプロジェクトでコンパイルが通らなくなるので、
    /// このパッケージは SDK にも Harmony にもコンパイル時依存を持たない
    /// （FBX Animation Baker が FBX Exporter を Reflection で呼ぶのと同じ方針）。
    /// </summary>
    internal static class HarmonyBridge
    {
        internal const string HarmonyId = "net.yozolab.vrcgizmoaccelerator";

        private static Type _harmonyType;
        private static Type _harmonyMethodType;
        private static MethodInfo _patchMethod;
        private static MethodInfo _unpatchAllMethod;
        private static object _instance;
        private static bool _resolved;

        internal static string UnavailableReason { get; private set; }

        internal static bool Available
        {
            get { Resolve(); return _instance != null; }
        }

        private static Assembly FindHarmonyAssembly()
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "0Harmony");
            if (loaded != null) return loaded;

            // まだ読み込まれていないだけの可能性がある。SDK が置いている場所を探す。
            foreach (var path in Directory.GetFiles("Packages", "0Harmony.dll", SearchOption.AllDirectories)
                         .Concat(Directory.Exists("Assets")
                             ? Directory.GetFiles("Assets", "0Harmony.dll", SearchOption.AllDirectories)
                             : Array.Empty<string>()))
            {
                try { return Assembly.LoadFrom(Path.GetFullPath(path)); }
                catch (Exception e) { Debug.LogWarning($"[VRC Gizmo Accelerator] {path} を読み込めなかった: {e.Message}"); }
            }

            return null;
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            Assembly harmonyAsm;
            try { harmonyAsm = FindHarmonyAssembly(); }
            catch (Exception e)
            {
                UnavailableReason = $"Harmony の探索に失敗した: {e.Message}";
                return;
            }

            if (harmonyAsm == null)
            {
                UnavailableReason = "0Harmony.dll が見つからない（VRChat SDK が入っていない可能性がある）";
                return;
            }

            _harmonyType = harmonyAsm.GetType("HarmonyLib.Harmony");
            _harmonyMethodType = harmonyAsm.GetType("HarmonyLib.HarmonyMethod");
            if (_harmonyType == null || _harmonyMethodType == null)
            {
                UnavailableReason = $"想定した Harmony の型が無い ({harmonyAsm.GetName().Name} {harmonyAsm.GetName().Version})";
                return;
            }

            _patchMethod = _harmonyType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "Patch"
                                     && m.GetParameters().Length >= 2
                                     && m.GetParameters()[0].ParameterType == typeof(MethodBase));
            _unpatchAllMethod = _harmonyType.GetMethod("UnpatchAll", new[] { typeof(string) });

            if (_patchMethod == null || _unpatchAllMethod == null)
            {
                UnavailableReason = "Harmony.Patch / UnpatchAll が見つからない";
                return;
            }

            try
            {
                _instance = Activator.CreateInstance(_harmonyType, HarmonyId);
            }
            catch (Exception e)
            {
                UnavailableReason = $"Harmony を初期化できなかった: {e.Message}";
                _instance = null;
            }
        }

        internal static string HarmonyVersion
        {
            get
            {
                Resolve();
                if (_harmonyType == null) return "(未検出)";
                var name = _harmonyType.Assembly.GetName();
                return $"{name.Name} {name.Version}";
            }
        }

        /// <summary>prefix / finalizer を当てる。どちらも null 可。</summary>
        internal static bool Patch(MethodBase original, MethodInfo prefix, MethodInfo finalizer = null)
        {
            Resolve();
            if (_instance == null || original == null) return false;

            try
            {
                var args = new object[_patchMethod.GetParameters().Length];
                args[0] = original;
                for (int i = 1; i < args.Length; i++) args[i] = null;

                // Patch(original, prefix, postfix, transpiler, finalizer) の並び。
                // 版によって finalizer が無いこともあるので、位置は名前で決める。
                var parameters = _patchMethod.GetParameters();
                for (int i = 1; i < parameters.Length; i++)
                {
                    switch (parameters[i].Name)
                    {
                        case "prefix":
                            args[i] = prefix != null ? Activator.CreateInstance(_harmonyMethodType, prefix) : null;
                            break;
                        case "finalizer":
                            args[i] = finalizer != null ? Activator.CreateInstance(_harmonyMethodType, finalizer) : null;
                            break;
                    }
                }

                _patchMethod.Invoke(_instance, args);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VRC Gizmo Accelerator] {Describe(original)} にパッチを当てられなかった: {(e.InnerException ?? e).Message}");
                return false;
            }
        }

        internal static void UnpatchAll()
        {
            Resolve();
            if (_instance == null) return;

            try { _unpatchAllMethod.Invoke(_instance, new object[] { HarmonyId }); }
            catch (Exception e)
            {
                Debug.LogWarning($"[VRC Gizmo Accelerator] パッチを外せなかった: {(e.InnerException ?? e).Message}");
            }
        }

        internal static string Describe(MethodBase method)
        {
            if (method == null) return "(null)";
            var ps = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name));
            return $"{method.DeclaringType?.FullName}.{method.Name}({ps})";
        }
    }
}
