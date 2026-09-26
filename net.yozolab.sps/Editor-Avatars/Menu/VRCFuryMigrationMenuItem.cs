using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YozoLab.SPS.Component;
using YozoLab.SPS.Utils;
using Object = UnityEngine.Object;

namespace YozoLab.SPS.Menu {
    /**
     * VRCFury の SPS コンポーネントを、このパッケージのものへ移し替える。
     *
     * 移植したコンポーネントはフィールドの構成が VRCFury のものと同じなので、設定を JSON として
     * 読み出し、中の型名（深度アクションなど SerializeReference の中身）を付け替えて書き込めば、
     * そのまま写せる。移し替えた元のコンポーネントは削除する（両方残すと、VRCFury が入った環境では
     * SPS が二重にビルドされる）。Undo で戻せる。
     *
     * VRCFury がプロジェクトに入っている間しか使えない。入っていないと、元のコンポーネントは
     * 型が分からない（Missing Script）ので中身を読めない。
     */
    internal static class VRCFuryMigrationMenuItem {
        private const string MenuPath = MenuItems.sps + "Migrate from VRCFury SPS";
        private const string UndoName = "Migrate from VRCFury SPS";

        private static readonly (string from, Type to)[] ComponentMap = {
            ("VF.Component.VRCFuryHapticPlug", typeof(SpsPlug)),
            ("VF.Component.VRCFuryHapticSocket", typeof(SpsSocket)),
            ("VF.Component.VRCFuryHapticTouchReceiver", typeof(SpsTouchReceiver)),
            ("VF.Component.VRCFuryHapticTouchSender", typeof(SpsTouchSender)),
        };

        private const string VRCFuryContainerType = "VF.Model.VRCFury";
        private const string VRCFurySpsOptionsType = "VF.Model.Feature.SpsOptions";

        [MenuItem(MenuPath, priority = MenuItems.upgradeLegacyHapticsPriority + 1)]
        private static void Run() {
            var roots = Selection.gameObjects;
            if (roots.Length == 0) {
                DialogUtils.DisplayDialog("Migrate from VRCFury SPS", "移し替えたいアバター（またはオブジェクト）を選んでから実行してください。", "OK");
                return;
            }

            var containerType = FindType(VRCFuryContainerType);
            var mapped = ComponentMap
                .Select(m => (from: FindType(m.from), m.to))
                .Where(m => m.from != null)
                .ToArray();
            if (mapped.Length == 0) {
                DialogUtils.DisplayDialog(
                    "Migrate from VRCFury SPS",
                    "VRCFury が見つかりませんでした。\n\n" +
                    "VRCFury のコンポーネントは、VRCFury が入っていないと中身を読めません（Missing Script になります）。" +
                    "移し替えるときだけ VRCFury を入れて実行し、終わったら外してください。",
                    "OK");
                return;
            }

            var targets = new List<(UnityEngine.Component source, Type to)>();
            foreach (var root in roots) {
                foreach (var (from, to) in mapped) {
                    foreach (var c in root.GetComponentsInChildren(from, true)) targets.Add((c, to));
                }
            }
            var optionSources = new List<UnityEngine.Component>();
            if (containerType != null) {
                foreach (var root in roots) {
                    foreach (var c in root.GetComponentsInChildren(containerType, true)) {
                        if (GetContentTypeName(c) == VRCFurySpsOptionsType) optionSources.Add(c);
                    }
                }
            }

            if (targets.Count == 0 && optionSources.Count == 0) {
                DialogUtils.DisplayDialog("Migrate from VRCFury SPS", "選んだ範囲に VRCFury の SPS コンポーネントはありませんでした。", "OK");
                return;
            }

            var summary = targets
                .GroupBy(t => t.to.Name)
                .Select(g => $"{g.Key}: {g.Count()}")
                .Concat(optionSources.Count > 0 ? new[] { $"SPS Options: {optionSources.Count}" } : new string[] { })
                .Join('\n');
            var ok = DialogUtils.DisplayDialog(
                "Migrate from VRCFury SPS",
                "次の VRCFury の SPS コンポーネントを YozoLab SPS のものに移し替え、元のコンポーネントを削除します。\n" +
                "（Undo で戻せます）\n\n" + summary,
                "移し替える",
                "キャンセル");
            if (!ok) return;

            Undo.SetCurrentGroupName(UndoName);
            var group = Undo.GetCurrentGroup();
            var failures = new List<string>();

            foreach (var (source, to) in targets) {
                try {
                    var json = RemapTypes(EditorJsonUtility.ToJson(source));
                    var created = Undo.AddComponent(source.gameObject, to);
                    EditorJsonUtility.FromJsonOverwrite(json, created);
                    EditorUtility.SetDirty(created);
                    Undo.DestroyObjectImmediate(source);
                } catch (Exception e) {
                    failures.Add($"{source.gameObject.name} ({source.GetType().Name}): {e.Message}");
                }
            }

            foreach (var source in optionSources) {
                try {
                    var content = new SerializedObject(source).FindProperty("content")?.managedReferenceValue;
                    if (content == null) continue;
                    var json = RemapTypes(EditorJsonUtility.ToJson(content));
                    var created = Undo.AddComponent<SpsOptionsComponent>(source.gameObject);
                    EditorJsonUtility.FromJsonOverwrite(json, created.options);
                    EditorUtility.SetDirty(created);
                    Undo.DestroyObjectImmediate(source);
                } catch (Exception e) {
                    failures.Add($"{source.gameObject.name} (SPS Options): {e.Message}");
                }
            }

            Undo.CollapseUndoOperations(group);

            if (failures.Count > 0) {
                DialogUtils.DisplayDialog("Migrate from VRCFury SPS", "一部を移し替えられませんでした。\n\n" + failures.Join('\n'), "OK");
            } else {
                DialogUtils.DisplayDialog("Migrate from VRCFury SPS", "移し替えました。\n\n" + summary, "OK");
            }
        }

        /**
         * JSON の中の型名を、VRCFury のものからこのパッケージのものへ付け替える。
         * SerializeReference の中身は {"class":..,"ns":..,"asm":..} の形で型を持っている。
         */
        internal static string RemapTypes(string json) {
            // コンポーネント自体の情報（どのスクリプトか、どの GameObject か）は写さない。
            // 写すと、移し替え先のスクリプト参照が VRCFury のものに書き換わってしまう。
            foreach (var key in new[] { "m_Script", "m_GameObject", "m_PrefabInstance", "m_PrefabAsset", "m_CorrespondingSourceObject" }) {
                json = System.Text.RegularExpressions.Regex.Replace(json, "\"" + key + "\":\\{[^}]*\\},?", "");
            }
            return json
                .Replace("\"ns\":\"VF.", "\"ns\":\"YozoLab.SPS.")
                .Replace("\"ns\":\"VF\"", "\"ns\":\"YozoLab.SPS\"")
                .Replace("\"asm\":\"VRCFury\"", "\"asm\":\"YozoLab.SPS.Runtime\"");
        }

        private static string GetContentTypeName(UnityEngine.Component vrcfury) {
            var content = new SerializedObject(vrcfury).FindProperty("content")?.managedReferenceValue;
            return content?.GetType().FullName;
        }

        private static Type FindType(string fullName) {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                var type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }
    }
}
