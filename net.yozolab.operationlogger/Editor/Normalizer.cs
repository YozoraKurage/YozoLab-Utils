using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace YozoLab.OperationLogger
{
    /// <summary>
    /// 生イベントをセマンティックな形へ変換する層。
    /// instanceID → 階層パス("Avatar/Armature/Hips")、値の人間化を担当する。
    /// GUID や instanceID をそのままログに出さないこと(読む側に意味がない)。
    /// </summary>
    internal static class Normalizer
    {
        // 破壊済みオブジェクトのパス解決用キャッシュ(id→path)。
        // 厳密な LRU は過剰なので、上限超過で全クリアする素朴な方式にしている。
        private const int CacheCap = 4096;
        private static readonly Dictionary<int, string> pathCache = new Dictionary<int, string>();

        public static void ClearCache() => pathCache.Clear();

        /// <summary>生存中なら実解決、破壊済みならキャッシュから解決する。</summary>
        public static string ResolveInstanceId(int instanceId)
        {
            var o = EditorUtility.InstanceIDToObject(instanceId);
            if (o != null) return Resolve(o);
            return pathCache.TryGetValue(instanceId, out var cached) ? cached : "(unknown)";
        }

        public static string Resolve(UnityEngine.Object o)
        {
            if (o == null) return null;
            string path;
            var c = o as Component;
            if (c != null) path = GameObjectPath(c.gameObject);
            else if (o is GameObject go) path = GameObjectPath(go);
            else
            {
                // シーン外(アセット)はアセットパス、それも無ければ名前。
                string ap = AssetDatabase.GetAssetPath(o);
                path = string.IsNullOrEmpty(ap) ? o.name : ap;
            }
            Remember(o.GetInstanceID(), path);
            return path;
        }

        private static string GameObjectPath(GameObject go)
        {
            var names = new List<string>(8);
            for (Transform t = go.transform; t != null; t = t.parent) names.Add(t.name);
            names.Reverse();
            string path = string.Join("/", names);

            // プレハブ編集モード中はシーンと区別できるようプレフィクスを付ける。
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && go.scene == stage.scene) path = "Prefab:" + path;
            return path;
        }

        private static void Remember(int id, string path)
        {
            if (pathCache.Count >= CacheCap) pathCache.Clear();
            pathCache[id] = path;
        }

        /// <summary>
        /// PropertyModification の値を JSON トークン(数値 or 文字列)へ人間化する。
        /// 数値は 3 桁丸め、文字列は 80 字切詰、オブジェクト参照はパス名。
        /// </summary>
        public static string HumanizeValue(PropertyModification pm)
        {
            if (pm == null) return null;
            if (pm.objectReference != null) return OpEvent.Quote(Resolve(pm.objectReference));
            string v = pm.value;
            if (string.IsNullOrEmpty(v)) return "\"\"";
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                return d.ToString("0.###", CultureInfo.InvariantCulture);
            return OpEvent.Quote(OpEvent.Truncate(v, 80));
        }

        /// <summary>
        /// プロパティパスの可読化。基本は生パスのまま(真実性優先)だが、
        /// VRChat アバター作業で頻出するブレンドシェイプだけは名前へ解決する。
        /// </summary>
        public static string FriendlyProp(UnityEngine.Object target, string propertyPath)
        {
            const string bs = "m_BlendShapeWeights.Array.data[";
            if (propertyPath != null && propertyPath.StartsWith(bs, StringComparison.Ordinal)
                && target is SkinnedMeshRenderer smr && smr != null && smr.sharedMesh != null)
            {
                int end = propertyPath.IndexOf(']', bs.Length);
                if (end > 0
                    && int.TryParse(propertyPath.Substring(bs.Length, end - bs.Length), out int idx)
                    && idx >= 0 && idx < smr.sharedMesh.blendShapeCount)
                    return "blendShape." + smr.sharedMesh.GetBlendShapeName(idx);
            }
            return propertyPath;
        }
    }

    /// <summary>
    /// prop / struct / undo イベントのコアレッシング(束ね)。本パッケージの品質の要。
    ///
    /// スライダードラッグ 1 回で postprocessModifications は数百回発火する。
    /// 「同一ターゲット・同一プロパティの連続変更」を 初回値→最終値+回数+所要秒 の
    /// 1 行に畳むことで、ログをトークン効率よく・意図が読める粒度に保つ。
    /// </summary>
    internal static class Coalescer
    {
        // ------------------------------------------------------------
        // prop(プロパティ変更)
        // ------------------------------------------------------------

        private sealed class PendingProp
        {
            public int targetId;
            public string objPath, comp, prop, from, to, undoLabel;
            public int n;
            public double firstT, lastT;
            public DateTime firstWall;
        }

        private const int MaxPending = 64;
        private static readonly Dictionary<(int id, string prop), PendingProp> props
            = new Dictionary<(int, string), PendingProp>();

        public static void AddProp(int targetId, string objPath, string comp,
            string propRaw, string propFriendly, string from, string to, string undoLabel)
        {
            double now = EditorApplication.timeSinceStartup;
            var key = (targetId, propRaw);
            if (props.TryGetValue(key, out var p))
            {
                if (p.undoLabel == undoLabel)
                {
                    p.to = to;
                    p.n++;
                    p.lastT = now;
                    return;
                }
                // 別の操作(Undo グループ)に移った → 前のジェスチャを確定
                props.Remove(key);
                FlushEntries(new List<PendingProp>(1) { p });
            }
            if (props.Count >= MaxPending) FlushOldestProp();
            props[key] = new PendingProp
            {
                targetId = targetId, objPath = objPath, comp = comp, prop = propFriendly,
                from = from, to = to, undoLabel = undoLabel,
                n = 1, firstT = now, lastT = now, firstWall = DateTime.Now,
            };
        }

        private static void FlushOldestProp()
        {
            (int, string)? oldestKey = null;
            double oldest = double.MaxValue;
            foreach (var kv in props)
                if (kv.Value.firstT < oldest) { oldest = kv.Value.firstT; oldestKey = kv.Key; }
            if (oldestKey == null) return;
            var p = props[oldestKey.Value];
            props.Remove(oldestKey.Value);
            FlushEntries(new List<PendingProp>(1) { p });
        }

        /// <summary>
        /// フラッシュ時の二段整形:
        /// ① ベクトル合成 — 同一ターゲットの .x/.y/.z/.w 成分エントリを 1 行に(Transform ドラッグが 3 行に割れるのを防ぐ)
        /// ② 多対象集約 — 同 (comp, prop, undo) が 3 対象以上なら 1 行に(=自動化候補シグナル)
        /// </summary>
        private static void FlushEntries(List<PendingProp> list)
        {
            if (list.Count == 0) return;

            // ① ベクトル合成
            var merged = new List<PendingProp>(list.Count);
            var vecGroups = new Dictionary<(int, string, string, string), PendingProp[]>();
            foreach (var p in list)
            {
                int vi = VecIndex(p.prop, out string prefix);
                if (vi < 0) { merged.Add(p); continue; }
                var key = (p.targetId, p.comp, prefix, p.undoLabel);
                if (!vecGroups.TryGetValue(key, out var arr)) vecGroups[key] = arr = new PendingProp[4];
                arr[vi] = p;
            }
            foreach (var kv in vecGroups)
            {
                var arr = kv.Value;
                var present = arr.Where(a => a != null).ToList();
                if (present.Count <= 1) { merged.AddRange(present); continue; }
                var rep = present[0];
                merged.Add(new PendingProp
                {
                    targetId = rep.targetId, objPath = rep.objPath, comp = rep.comp,
                    prop = kv.Key.Item3,
                    from = "[" + string.Join(",", arr.Where(a => a != null).Select(a => a.from)) + "]",
                    to = "[" + string.Join(",", arr.Where(a => a != null).Select(a => a.to)) + "]",
                    undoLabel = rep.undoLabel,
                    n = present.Sum(a => a.n),
                    firstT = present.Min(a => a.firstT),
                    lastT = present.Max(a => a.lastT),
                    firstWall = present.Min(a => a.firstWall),
                });
            }

            // ② 多対象集約
            foreach (var g in merged.GroupBy(p => (p.comp, p.prop, p.undoLabel)))
            {
                var items = g.ToList();
                int targets = items.Select(p => p.targetId).Distinct().Count();
                if (items.Count >= 3 && targets >= 3)
                {
                    var rep = items[0];
                    OpLogger.Emit(OpEvent.New(OpType.Prop, items.Min(p => p.firstWall))
                        .Str("obj", rep.objPath + " (+" + (targets - 1) + ")")
                        .Int("targets", targets)
                        .Str("comp", rep.comp).Str("prop", rep.prop)
                        .Raw("from", rep.from).Raw("to", rep.to)
                        .Int("n", items.Sum(p => p.n), skipIf: 1)
                        .Num("dur", items.Max(p => p.lastT) - items.Min(p => p.firstT), skipIfZero: true)
                        .Str("undo", rep.undoLabel));
                }
                else
                {
                    foreach (var p in items)
                        OpLogger.Emit(OpEvent.New(OpType.Prop, p.firstWall)
                            .Str("obj", p.objPath)
                            .Str("comp", p.comp).Str("prop", p.prop)
                            .Raw("from", p.from).Raw("to", p.to)
                            .Int("n", p.n, skipIf: 1)
                            .Num("dur", p.lastT - p.firstT, skipIfZero: true)
                            .Str("undo", p.undoLabel));
                }
            }
        }

        private static readonly string[] VecSuffixes = { ".x", ".y", ".z", ".w" };

        private static int VecIndex(string prop, out string prefix)
        {
            prefix = null;
            if (prop == null) return -1;
            for (int i = 0; i < VecSuffixes.Length; i++)
            {
                if (prop.EndsWith(VecSuffixes[i], StringComparison.Ordinal) && prop.Length > 2)
                {
                    prefix = prop.Substring(0, prop.Length - 2);
                    return i;
                }
            }
            return -1;
        }

        // ------------------------------------------------------------
        // struct(階層構造変更)— op 種別ごとに 500ms 窓で n を加算
        // ------------------------------------------------------------

        private sealed class PendingStruct
        {
            public string op, obj, from, to;
            public int n;
            public double firstT, lastT;
            public DateTime firstWall;
        }

        private static readonly Dictionary<string, PendingStruct> structs
            = new Dictionary<string, PendingStruct>();

        public static void AddStruct(string op, string obj, string from = null, string to = null)
        {
            double now = EditorApplication.timeSinceStartup;
            if (structs.TryGetValue(op, out var s))
            {
                if (now - s.lastT <= 0.5) { s.n++; s.lastT = now; return; }
                structs.Remove(op);
                EmitStruct(s);
            }
            structs[op] = new PendingStruct
            {
                op = op, obj = obj, from = from, to = to,
                n = 1, firstT = now, lastT = now, firstWall = DateTime.Now,
            };
        }

        private static void EmitStruct(PendingStruct s)
        {
            OpLogger.Emit(OpEvent.New(OpType.Struct, s.firstWall)
                .Str("op", s.op).Str("obj", s.obj)
                .Str("from", s.from).Str("to", s.to)
                .Int("n", s.n, skipIf: 1)
                .Num("dur", s.lastT - s.firstT, skipIfZero: true));
        }

        // ------------------------------------------------------------
        // undo(Undo/Redo バースト)— 同方向の連打を 1 行に(undo 嵐の検出用)
        // ------------------------------------------------------------

        private sealed class PendingUndo
        {
            public bool redo;
            public string label;
            public int n;
            public double firstT, lastT;
            public DateTime firstWall;
        }

        private static PendingUndo undoPending;

        public static void AddUndo(bool redo, string label)
        {
            double now = EditorApplication.timeSinceStartup;
            if (undoPending != null && undoPending.redo == redo)
            {
                // 連打はラベルが 1 手ごとに変わるため、方向が同じなら束ねる(ラベルは初回のもの)
                undoPending.n++;
                undoPending.lastT = now;
                return;
            }
            FlushUndo();
            undoPending = new PendingUndo
            {
                redo = redo, label = label,
                n = 1, firstT = now, lastT = now, firstWall = DateTime.Now,
            };
        }

        private static void FlushUndo()
        {
            if (undoPending == null) return;
            var p = undoPending;
            undoPending = null;
            OpLogger.Emit(OpEvent.New(OpType.UndoRedo, p.firstWall)
                .Str("op", p.redo ? "redo" : "undo").Str("label", p.label)
                .Int("n", p.n, skipIf: 1)
                .Num("dur", p.lastT - p.firstT, skipIfZero: true));
        }

        // ------------------------------------------------------------
        // フラッシュ制御
        // ------------------------------------------------------------

        /// <summary>update tick から呼ばれる。アイドルになったエントリを確定する。</summary>
        public static void Tick(double now)
        {
            double window = OpLoggerSettings.instance.coalesceWindowMs / 1000.0;

            List<PendingProp> dueProps = null;
            List<(int, string)> dueKeys = null;
            foreach (var kv in props)
            {
                if (now - kv.Value.lastT <= window) continue;
                (dueProps ??= new List<PendingProp>()).Add(kv.Value);
                (dueKeys ??= new List<(int, string)>()).Add(kv.Key);
            }
            if (dueProps != null)
            {
                foreach (var k in dueKeys) props.Remove(k);
                FlushEntries(dueProps); // 同時にアイドルになった群はまとめて整形(多対象集約が効く)
            }

            List<string> dueStructs = null;
            foreach (var kv in structs)
                if (now - kv.Value.lastT > 0.5) (dueStructs ??= new List<string>()).Add(kv.Key);
            if (dueStructs != null)
                foreach (var k in dueStructs) { var s = structs[k]; structs.Remove(k); EmitStruct(s); }

            if (undoPending != null && now - undoPending.lastT > 2.0) FlushUndo();
        }

        /// <summary>編集系(prop/struct)のみ確定。選択変更・Undo 発火時に呼ぶ。</summary>
        public static void FlushEdits()
        {
            if (props.Count > 0)
            {
                var all = props.Values.ToList();
                props.Clear();
                FlushEntries(all);
            }
            if (structs.Count > 0)
            {
                var all = structs.Values.ToList();
                structs.Clear();
                foreach (var s in all) EmitStruct(s);
            }
        }

        /// <summary>全 pending を確定。シーン保存・play 遷移・リロード前・セッション終了時に呼ぶ。</summary>
        public static void FlushAll()
        {
            FlushEdits();
            FlushUndo();
        }
    }
}
