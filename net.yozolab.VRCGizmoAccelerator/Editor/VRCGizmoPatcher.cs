using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// パッチの当先を見つけて Harmony に流し込む。
    ///
    /// 当てるのは 2 種類だけ。
    ///   1) ギズモの入口（VRChat SDK の OnDrawGizmos 系）… バッチ区間の開始/終了
    ///   2) 描画プリミティブ（Handles と SDK の HandlesUtil）… GL 発行の横取り
    ///
    /// ジオメトリは SDK 自身に計算させたままなので、見た目は元と同じになる。
    /// 変わるのは「GL への発行のまとめ方」だけ。
    /// </summary>
    [InitializeOnLoad]
    internal static class VRCGizmoPatcher
    {
        /// <summary>入口 1 つ分。ウィンドウでの表示と個別 ON/OFF に使う。</summary>
        internal sealed class Target
        {
            public string group;          // PhysBone / Contact / Constraint
            public string label;          // 表示名
            public string typeName;       // アセンブリ修飾なしの型名
            public string methodName;
            public MethodBase method;     // 解決できなければ null
            public bool patched;
            public string skipReason;     // 当てなかった理由（外側で包んでいる等）
        }

        private static readonly List<Target> Targets = new List<Target>();
        private static readonly List<string> PrimitiveLog = new List<string>();

        internal static IReadOnlyList<Target> AllTargets => Targets;
        internal static IReadOnlyList<string> PrimitiveStatus => PrimitiveLog;
        internal static bool Installed { get; private set; }
        internal static int PatchedPrimitiveCount { get; private set; }

        static VRCGizmoPatcher()
        {
            // ドメインリロード直後は他アセンブリの読み込みが終わっていないことがあるので、
            // 1 フレーム遅らせてから当てる。
            EditorApplication.delayCall += () =>
            {
                if (VRCGizmoAcceleratorSettings.instance.enabled) Install();
            };

            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                if (Installed) Uninstall();
            };
        }

        // ---- 型・メソッド探索 -------------------------------------------------

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        private static readonly BindingFlags AnyStatic =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly BindingFlags AnyMethod =
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static IEnumerable<MethodInfo> FindMethods(Type type, string name)
        {
            if (type == null) return Enumerable.Empty<MethodInfo>();
            return type.GetMethods(AnyMethod).Where(m => m.Name == name);
        }

        /// <summary>
        /// prefix の引数が元メソッドに（名前と型で）存在するかを確かめる。
        /// Harmony は名前で束ねるので、Unity や SDK 側で引数名が変わっていたら
        /// 例外になる前にここで弾いて、そのプリミティブだけ素通しにする。
        /// </summary>
        private static bool SignatureMatches(MethodBase original, MethodInfo prefix)
        {
            var originalParams = original.GetParameters();
            foreach (var p in prefix.GetParameters())
            {
                if (p.Name.StartsWith("__")) continue; // __result などは Harmony の予約

                var match = originalParams.FirstOrDefault(o => o.Name == p.Name);
                if (match == null) return false;

                // object で受ける分には、参照型ならそのまま渡せる
                if (p.ParameterType == typeof(object) && !match.ParameterType.IsValueType) continue;
                if (match.ParameterType != p.ParameterType) return false;
            }

            if (original is MethodInfo mi && mi.ReturnType != typeof(void))
            {
                var result = prefix.GetParameters().FirstOrDefault(p => p.Name == "__result");
                if (result == null) return false;
                if (result.ParameterType != mi.ReturnType.MakeByRefType()) return false;
            }

            return true;
        }

        private static MethodInfo Prefix(string name) =>
            typeof(HandlesInterceptor).GetMethod(name, BindingFlags.Static | BindingFlags.Public);

        // ---- インストール -----------------------------------------------------

        internal static void Install()
        {
            if (Installed) return;

            Targets.Clear();
            PrimitiveLog.Clear();
            ScopeProfiler.Clear();
            PatchedPrimitiveCount = 0;

            if (!HarmonyBridge.Available)
            {
                PrimitiveLog.Add($"Harmony が使えない: {HarmonyBridge.UnavailableReason}");
                return;
            }

            if (!HandlesMaterial.Available)
            {
                PrimitiveLog.Add("HandleUtility.ApplyWireMaterial が見つからない（この Unity では使えない）");
                return;
            }

            PatchPrimitives();

            if (PatchedPrimitiveCount == 0)
            {
                // プリミティブを 1 つも横取りできないなら、区間だけ張っても意味が無い。
                // 何もしないほうが安全なので、ここで降りる。
                HarmonyBridge.UnpatchAll();
                PrimitiveLog.Add("横取りできるプリミティブが無かったため、パッチを当てていない");
                return;
            }

            CollectTargets();
            PatchScopes();

            Installed = true;
            GizmoBatch.Reset();
            PhysBoneInitCache.InvalidateAll();
            PhysBoneInitCache.ResetStats();
            GizmoGeometryCache.Clear();
            GizmoGeometryCache.ResetStats();
            GizmoScopes.Reset();
            GizmoSubmitter.ResetAvailability();
            GizmoCommandBuffer.Clear();
        }

        private static void PatchPrimitives()
        {
            // Handles 側は既定では触らない。
            //
            // これらは Unity 本体のネイティブ実装で、円弧の分割もネイティブでやっている。
            // 横取りすると SetPass は減らせるが、その分割を C# で書き直すことになり、
            // 実測では 2000 円弧で 2ms ほど余計にかかった。図形数が多くて SetPass が
            // 効いている場合だけ得になるので、明示的に有効にしたときだけ当てる。
            if (VRCGizmoAcceleratorSettings.instance.interceptUnityHandles)
            {
                // thickness 付きのオーバーロードもまとめて当てる
                // （小さいメソッドは JIT にインライン化され得るので、内側も押さえておく）。
                PatchPrimitive(typeof(Handles), "DrawLine", "DrawLine_Prefix");
                PatchPrimitive(typeof(Handles), "DrawWireArc", "DrawWireArc_Prefix");
                PatchPrimitive(typeof(Handles), "DrawWireDisc", "DrawWireDisc_Prefix");
                PatchPrimitive(typeof(Handles), "DrawSolidArc", "DrawSolidArc_Prefix");
                PatchPrimitive(typeof(Handles), "DrawSolidDisc", "DrawSolidDisc_Prefix");
            }
            else
            {
                PrimitiveLog.Add("- UnityEditor.Handles は横取りしない（既定。ネイティブ実装のほうが速いため）");
            }

            // VRChat SDK 側。名前空間なしのグローバル型。
            var handlesUtil = FindType("HandlesUtil");
            if (handlesUtil == null)
            {
                PrimitiveLog.Add("HandlesUtil が見つからない（VRChat SDK 未導入か、構成が変わった）");
                return;
            }

            PatchPrimitive(handlesUtil, "DrawLineBatched", "DrawLineBatched_Prefix");
            PatchPrimitive(handlesUtil, "DrawSphereBatched", "DrawSphereBatched_Prefix");
            PatchPrimitive(handlesUtil, "DrawCapsuleBatched", "DrawCapsuleBatched_Prefix");
            PatchPrimitive(handlesUtil, "ApplyWireMaterial", "ApplyWireMaterial_Prefix");

            // 図形を組み立てる側。描画イベントでないときは、中の座標計算ごと省く。
            foreach (var name in new[]
                     {
                         "DrawWireSphere", "DrawWireCapsule", "DrawWireCylinder",
                         "DrawWireSquare", "DrawWireCube", "DrawWirePlane",
                         "DrawWireAngleCone", "DrawSolidAngleCone",
                     })
            {
                PatchPrimitive(handlesUtil, name, "SkipWhenNotDrawing_Prefix");
            }

            // カメラ依存の値を参照したかどうかを見るため（値には手を触れない）
            PatchPrimitive(typeof(HandleUtility), "GetHandleSize", "GetHandleSize_Prefix");

            // 即時描画の発行回数を数える診断（数えるだけで挙動は変えない）
            if (HarmonyBridge.Patch(ImmediateDrawDiagnostics.Target, ImmediateDrawDiagnostics.Prefix))
            {
                PrimitiveLog.Add("○ HandleUtility.ApplyWireMaterial: 発行回数の計測");
            }

            PatchBoneInit();
        }

        /// <summary>
        /// ギズモ描画のたびに走るボーン構造の作り直しを省く。
        /// プリミティブの横取りと違って、これは描画そのものではなく前処理の削減。
        /// </summary>
        private static void PatchBoneInit()
        {
            var physBoneBase = FindType("VRC.Dynamics.VRCPhysBoneBase");
            if (physBoneBase == null)
            {
                PrimitiveLog.Add("× VRCPhysBoneBase が見つからない（ボーン構造のキャッシュは無効）");
                return;
            }

            var prefix = typeof(PhysBoneInitCache).GetMethod(
                "InitTransforms_Prefix", BindingFlags.Static | BindingFlags.Public);
            var target = FindMethods(physBoneBase, "InitTransforms").FirstOrDefault();

            if (target == null || prefix == null || !SignatureMatches(target, prefix))
            {
                PrimitiveLog.Add("× VRCPhysBoneBase.InitTransforms に合わせられない（ボーン構造のキャッシュは無効）");
                return;
            }

            if (HarmonyBridge.Patch(target, prefix))
            {
                PatchedPrimitiveCount++;
                PrimitiveLog.Add("○ VRCPhysBoneBase.InitTransforms: ボーン構造の作り直しを省く");
            }
        }

        private static void PatchPrimitive(Type type, string methodName, string prefixName)
        {
            var prefix = Prefix(prefixName);
            if (prefix == null)
            {
                PrimitiveLog.Add($"× {type.Name}.{methodName}: prefix {prefixName} が無い");
                return;
            }

            var candidates = FindMethods(type, methodName).ToList();
            if (candidates.Count == 0)
            {
                PrimitiveLog.Add($"× {type.Name}.{methodName}: メソッドが見つからない");
                return;
            }

            int done = 0, skipped = 0;
            foreach (var candidate in candidates)
            {
                if (!SignatureMatches(candidate, prefix)) { skipped++; continue; }
                if (HarmonyBridge.Patch(candidate, prefix)) done++;
            }

            PatchedPrimitiveCount += done;

            var note = skipped > 0 ? $"（{skipped} 個のオーバーロードは引数が合わず素通し）" : "";
            PrimitiveLog.Add(done > 0
                ? $"○ {type.Name}.{methodName}: {done} 個にパッチ {note}"
                : $"× {type.Name}.{methodName}: 当てられるオーバーロードが無い {note}");
        }

        private static void CollectTargets()
        {
            void Add(string group, string label, string typeName, string methodName)
            {
                var type = FindType(typeName);
                var method = FindMethods(type, methodName).FirstOrDefault();
                Targets.Add(new Target
                {
                    group = group,
                    label = label,
                    typeName = typeName,
                    methodName = methodName,
                    method = method,
                });
            }

            Add("PhysBone", "PhysBone", "VRC.SDK3.Dynamics.PhysBone.VRCPhysBoneEditor", "OnDrawGizmos");
            Add("PhysBone", "PhysBone Collider (選択中)", "VRC.SDK3.Dynamics.PhysBone.VRCPhysBoneColliderEditor", "OnDrawGizmos_Selected");
            Add("PhysBone", "PhysBone Collider (アクティブ)", "VRC.SDK3.Dynamics.PhysBone.VRCPhysBoneColliderEditor", "OnDrawGizmos_Active");
            Add("PhysBone", "PhysBoneManager (再生中)", "VRC.Dynamics.PhysBoneManager", "OnDrawGizmos");
            Add("Contact", "Contact (全体)", "VRC.SDK3.Dynamics.Contact.VRCContactBaseEditor", "OnDrawGizmo_Full");
            Add("Contact", "Contact (半分)", "VRC.SDK3.Dynamics.Contact.VRCContactBaseEditor", "OnDrawGizmo_Half");
            Add("Contact", "ContactManager (再生中)", "VRC.Dynamics.ContactManager", "OnDrawGizmos");
            Add("Constraint", "Constraint", "VRC.Dynamics.VRCConstraintBase", "OnDrawGizmosSelected");

            // Avatar Descriptor のコライダー表示。これはギズモではなく OnSceneGUI で、
            // HandlesUtil.DrawWireCapsule を何本も呼ぶ（1 本ごとに SetPass が入る）。
            Add("Avatar", "Avatar Descriptor (コライダー)", "AvatarDescriptorEditor3", "DrawScene_Colliders");
            Add("Avatar", "Avatar Descriptor (シーン GUI)", "AvatarDescriptorEditor3", "OnSceneGUI");
        }

        private static void PatchScopes()
        {
            var gizmoBegin = Prefix("GizmoScope_Prefix");
            var guiBegin = Prefix("GuiScope_Prefix");
            var end = Prefix("GizmoScope_Finalizer");
            var settings = VRCGizmoAcceleratorSettings.instance;

            // OnSceneGUI を包む場合は、その中に DrawScene_Colliders も入る。
            // 二重に包んでも入れ子の数え方で正しく動くが、無駄なので外側だけにする。
            bool hasOuterSceneGui = Targets.Any(
                t => t.group == "Avatar" && t.methodName == "OnSceneGUI" && t.method != null);

            foreach (var target in Targets)
            {
                if (target.method == null) continue;
                if (!settings.IsGroupEnabled(target.group)) continue;

                // Avatar Descriptor のコライダーは Handles 経由でしか描かれない。
                // Handles を横取りしない設定なら、区間を張っても何も起きない。
                if (target.group == "Avatar" && !settings.interceptUnityHandles)
                {
                    target.skipReason = "Handles の横取りが無効";
                    continue;
                }
                if (target.group == "Avatar" && target.methodName == "DrawScene_Colliders" && hasOuterSceneGui)
                {
                    target.skipReason = "外側の OnSceneGUI に含まれる";
                    continue;
                }

                // OnSceneGUI は描画以外のイベントでも呼ばれるので、遅延させない区間にする。
                // 対象コンポーネントを受け取れるなら、描画結果のキャッシュが使える。
                var first = target.method.GetParameters().FirstOrDefault();
                var begin = target.group == "Avatar" ? guiBegin
                    : first?.Name == "script" ? Prefix("GizmoScopeScript_Prefix")
                    : first?.Name == "target" ? Prefix("GizmoScopeTarget_Prefix")
                    : gizmoBegin;

                if (!SignatureMatches(target.method, begin)) begin = gizmoBegin;
                target.patched = HarmonyBridge.Patch(target.method, begin, end);
                if (target.patched) ScopeProfiler.Register(target.method, target.label);
            }
        }

        internal static void Uninstall()
        {
            HarmonyBridge.UnpatchAll();
            foreach (var t in Targets) t.patched = false;
            Installed = false;
            GizmoBatch.Reset();
            GizmoScopes.Reset();
            GizmoGeometryCache.Clear();
            GizmoCommandBuffer.Clear();
            TransformFingerprint.Clear();
            PhysBoneInitCache.InvalidateAll();
            SceneView.RepaintAll();
        }

        /// <summary>設定変更後に当て直す。</summary>
        internal static void Reinstall()
        {
            if (Installed) Uninstall();
            if (VRCGizmoAcceleratorSettings.instance.enabled) Install();
            SceneView.RepaintAll();
        }
    }
}
