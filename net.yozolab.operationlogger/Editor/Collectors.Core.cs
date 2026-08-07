using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YozoLab.OperationLogger
{
    /// <summary>
    /// 編集操作の中核コレクタ(構造変更・プロパティ変更・Undo/Redo・選択)。
    ///
    /// 役割分担:
    ///   ・ObjectChangeEvents      … 「何が起きたか」の骨格(生成/破棄/親変更など)
    ///   ・postprocessModifications … 「値がどう変わったか」(before/after が取れる唯一の場所)
    ///   ・Undo.GetCurrentGroupName … 操作の意図ラベル("Move" "Paste" など)
    /// 全ハンドラは例外を外へ漏らさない(エディタ操作を妨げないため)。
    /// </summary>
    internal static class CoreCollectors
    {
        private static bool subscribed;

        // 選択デバウンス(1 秒アイドルで最終選択のみ記録)
        private static List<string> pendingSel;
        private static int pendingSelTotal;
        private static double pendingSelT;

        public static void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            Undo.postprocessModifications += OnPostprocessModifications;
            Undo.undoRedoEvent += OnUndoRedo;
            Selection.selectionChanged += OnSelectionChanged;
        }

        public static void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed = false;
            ObjectChangeEvents.changesPublished -= OnChangesPublished;
            Undo.postprocessModifications -= OnPostprocessModifications;
            Undo.undoRedoEvent -= OnUndoRedo;
            Selection.selectionChanged -= OnSelectionChanged;
            pendingSel = null;
        }

        public static void Tick(double now)
        {
            if (pendingSel != null && now - pendingSelT > 1.0)
            {
                var sel = pendingSel;
                pendingSel = null;
                OpLogger.Emit(OpEvent.New(OpType.Sel)
                    .StrArray("obj", sel)
                    .Int("n", pendingSelTotal, skipIf: 1));
            }
        }

        // ------------------------------------------------------------
        // ObjectChangeEvents — 構造変更
        // ------------------------------------------------------------

        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            try
            {
                if (!OpLogger.EditsAllowed) return;
                // 注意: stream はこのコールバック内でのみ有効。保持せず即座に抽出する。
                for (int i = 0; i < stream.length; i++)
                {
                    switch (stream.GetEventType(i))
                    {
                        case ObjectChangeKind.CreateGameObjectHierarchy:
                        {
                            stream.GetCreateGameObjectHierarchyEvent(i, out var e);
                            Coalescer.AddStruct("create", Normalizer.ResolveInstanceId(e.instanceId));
                            break;
                        }
                        case ObjectChangeKind.DestroyGameObjectHierarchy:
                        {
                            stream.GetDestroyGameObjectHierarchyEvent(i, out var e);
                            Coalescer.AddStruct("destroy", Normalizer.ResolveInstanceId(e.instanceId));
                            break;
                        }
                        case ObjectChangeKind.ChangeGameObjectParent:
                        {
                            stream.GetChangeGameObjectParentEvent(i, out var e);
                            Coalescer.AddStruct("reparent",
                                Normalizer.ResolveInstanceId(e.instanceId),
                                ParentPath(e.previousParentInstanceId),
                                ParentPath(e.newParentInstanceId));
                            break;
                        }
                        case ObjectChangeKind.ChangeGameObjectStructure:
                        {
                            stream.GetChangeGameObjectStructureEvent(i, out var e);
                            Coalescer.AddStruct("structure", Normalizer.ResolveInstanceId(e.instanceId));
                            break;
                        }
                        case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                        {
                            stream.GetChangeGameObjectStructureHierarchyEvent(i, out var e);
                            Coalescer.AddStruct("structure", Normalizer.ResolveInstanceId(e.instanceId));
                            break;
                        }
                        case ObjectChangeKind.ChangeChildrenOrder:
                        {
                            // 注: 2022.3 にはルート直下の並べ替え専用イベントは無い
                            // (兄弟間の並べ替えはこの ChangeChildrenOrder が拾う)
                            stream.GetChangeChildrenOrderEvent(i, out var e);
                            Coalescer.AddStruct("reorder", Normalizer.ResolveInstanceId(e.instanceId));
                            break;
                        }
                        case ObjectChangeKind.UpdatePrefabInstances:
                        {
                            stream.GetUpdatePrefabInstancesEvent(i, out var e);
                            string first = e.instanceIds.Length > 0
                                ? Normalizer.ResolveInstanceId(e.instanceIds[0]) : null;
                            for (int k = 0; k < e.instanceIds.Length; k++)
                                Coalescer.AddStruct("prefab_update", first);
                            break;
                        }
                        // ChangeGameObjectOrComponentProperties は postprocessModifications 側で
                        // 値付きで捕捉できるため記録しない(重複防止)。
                        // CreateAssetObject / DestroyAssetObject / ChangeAssetObjectProperties は
                        // AssetPostprocessor 側でまとめて捕捉する。
                        // ChangeScene(dirty 化)は毎編集で発火するノイズのため無視。
                    }
                }
            }
            catch
            {
                // コレクタの例外でエディタ操作を妨げない
            }
        }

        private static string ParentPath(int instanceId)
            => instanceId == 0 ? "(root)" : Normalizer.ResolveInstanceId(instanceId);

        // ------------------------------------------------------------
        // Undo.postprocessModifications — プロパティ変更(値付き)
        // ------------------------------------------------------------

        private static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] mods)
        {
            try
            {
                if (OpLogger.EditsAllowed && mods != null)
                {
                    // ラベルは mod 到着時に取る(フラッシュ時にはグループが変わっている)
                    string label = Undo.GetCurrentGroupName();
                    foreach (var um in mods)
                    {
                        var cur = um.currentValue;
                        var prev = um.previousValue;
                        var target = cur != null && cur.target != null ? cur.target
                                   : prev != null ? prev.target : null;
                        if (target == null) continue;
                        if (target is OpLoggerSettings) continue; // 自分の設定保存は記録しない

                        string propRaw = cur != null ? cur.propertyPath
                                       : prev != null ? prev.propertyPath : null;
                        if (string.IsNullOrEmpty(propRaw)) continue;

                        Coalescer.AddProp(
                            target.GetInstanceID(),
                            Normalizer.Resolve(target),
                            target.GetType().Name,
                            propRaw,
                            Normalizer.FriendlyProp(target, propRaw),
                            Normalizer.HumanizeValue(prev),
                            Normalizer.HumanizeValue(cur),
                            label);
                    }
                }
            }
            catch
            {
                // 例外を漏らすとユーザーの Undo が壊れるため必ず握りつぶす
            }
            return mods; // 契約: 受け取った配列を必ずそのまま返す
        }

        // ------------------------------------------------------------
        // Undo/Redo 発火(undo 嵐 = 試行錯誤シグナル)
        // ------------------------------------------------------------

        private static void OnUndoRedo(in UndoRedoInfo info)
        {
            try
            {
                if (!OpLogger.IsRecording) return;
                Coalescer.FlushEdits(); // 直前の編集を先に確定して時系列を保つ
                Coalescer.AddUndo(info.isRedo, info.undoName);
            }
            catch { }
        }

        // ------------------------------------------------------------
        // 選択(ユーザーが何に注目しているかの文脈)
        // ------------------------------------------------------------

        private static void OnSelectionChanged()
        {
            try
            {
                if (!OpLogger.EditsAllowed || !OpLoggerSettings.instance.logSelection) return;
                Coalescer.FlushEdits(); // 対象が切り替わった = ジェスチャ終了とみなす

                var objs = Selection.objects;
                if (objs == null || objs.Length == 0) return; // 選択解除はノイズのため記録しない

                var list = new List<string>(Math.Min(objs.Length, 5));
                for (int i = 0; i < objs.Length && i < 5; i++)
                    list.Add(Normalizer.Resolve(objs[i]));
                pendingSel = list;
                pendingSelTotal = objs.Length;
                pendingSelT = EditorApplication.timeSinceStartup;
            }
            catch { }
        }
    }
}
