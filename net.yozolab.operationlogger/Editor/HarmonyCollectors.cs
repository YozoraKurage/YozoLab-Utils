// ============================================================================
// このファイルは Harmony(0Harmony.dll)を同梱する VRCSDK(com.vrchat.base)が
// 存在するときのみコンパイルされる。判定は asmdef の versionDefines に任せており、
// YOZOLAB_OPLOG_HARMONY はこのアセンブリのコンパイル時にだけ定義される
// (プロジェクトの Scripting Define Symbols には一切書き込まない)。
// 別のパッケージ経由で Harmony を導入している場合は asmdef に 1 エントリ足すこと。
// Harmony が無い環境ではメニュー/ショートカット捕捉(cmd イベント)だけが
// 無効になり、他のコレクタは通常どおり動作する。
// ============================================================================
#if YOZOLAB_OPLOG_HARMONY
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEngine;

namespace YozoLab.OperationLogger
{
    /// <summary>
    /// メニュー/ショートカット実行の捕捉(cmd イベント)。
    ///
    /// 制限(UnityCsReference 2022.3 で確認済み):
    /// メインメニューバーのマウスクリックはネイティブ MenuController がデリゲートを
    /// 直接呼び出すため managed 側のチョークポイントが存在せず、捕捉できない。
    /// その操作の「効果」は Undo グループ名 / struct / asset イベント側で捕捉される。
    /// 確実に取れるのは以下の 3 経路:
    ///   1. EditorApplication.ExecuteMenuItem  … API/ショートカット経由のメニュー実行
    ///   2. GenericMenu.CatchMenu              … Hierarchy 右クリック等のコンテキストメニュー
    ///   3. ShortcutManagement.Trigger         … ショートカット全般
    /// 各パッチは個別に縮退し、成否はセッションヘッダの cap に記録される。
    /// </summary>
    [InitializeOnLoad]
    internal static class MenuCaptureCollector
    {
        private const string HarmonyId = "net.yozolab.operationlogger.menucapture";

        private static string lastCmd;
        private static double lastCmdT;
        private static PropertyInfo entryIdentifierProp;
        private static PropertyInfo identifierPathProp;

        static MenuCaptureCollector()
        {
            OpCaps.Available = true;
            Harmony harmony;
            try
            {
                harmony = new Harmony(HarmonyId);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[OpLogger] Harmony 初期化に失敗(cmd 捕捉は無効のまま): " + e.Message);
                return;
            }
            PatchExecuteMenuItem(harmony);
            PatchGenericMenu(harmony);
            PatchShortcut(harmony);
        }

        private static HarmonyMethod Method(string name)
            => new HarmonyMethod(typeof(MenuCaptureCollector).GetMethod(
                name, BindingFlags.NonPublic | BindingFlags.Static));

        // ---------- 1. ExecuteMenuItem ----------

        private static void PatchExecuteMenuItem(Harmony harmony)
        {
            try
            {
                var target = typeof(EditorApplication).GetMethod(
                    "ExecuteMenuItem", BindingFlags.Public | BindingFlags.Static);
                if (target == null) throw new MissingMethodException("EditorApplication.ExecuteMenuItem");
                harmony.Patch(target, prefix: Method(nameof(ExecMenuPrefix)));
                OpCaps.Menu = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[OpLogger] ExecuteMenuItem のパッチに失敗(メニュー捕捉は無効): " + e.Message);
            }
        }

        private static void ExecMenuPrefix(string menuItemPath)
        {
            EmitCmd(menuItemPath, "menu");
        }

        // ---------- 2. GenericMenu(コンテキストメニュー) ----------

        private static void PatchGenericMenu(Harmony harmony)
        {
            try
            {
                var target = typeof(GenericMenu).GetMethod(
                    "CatchMenu", BindingFlags.NonPublic | BindingFlags.Instance);
                if (target == null) throw new MissingMethodException("GenericMenu.CatchMenu");
                harmony.Patch(target, postfix: Method(nameof(CatchMenuPostfix)));
                OpCaps.Ctx = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[OpLogger] GenericMenu のパッチに失敗(コンテキストメニュー捕捉は無効): " + e.Message);
            }
        }

        private static void CatchMenuPostfix(string[] options, int selected)
        {
            try
            {
                if (options != null && selected >= 0 && selected < options.Length)
                    EmitCmd(options[selected], "context");
            }
            catch { }
        }

        // ---------- 3. ショートカット ----------

        private static void PatchShortcut(Harmony harmony)
        {
            try
            {
                var triggerType = typeof(Editor).Assembly.GetType("UnityEditor.ShortcutManagement.Trigger");
                var target = triggerType?
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "ExecuteShortcut");
                if (target != null)
                {
                    harmony.Patch(target, postfix: Method(nameof(ShortcutPostfix)));
                    OpCaps.Sc = true;
                }
                else if (TrySubscribeInvokingAction(triggerType))
                {
                    OpCaps.Sc = true; // フォールバック: public event invokingAction 経由
                }
                else
                {
                    Debug.LogWarning("[OpLogger] ショートカット捕捉ポイントが見つかりません(無効のまま)");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[OpLogger] ショートカット捕捉のパッチに失敗: " + e.Message);
            }
        }

        private static void ShortcutPostfix(object __0)
        {
            HandleShortcutEntry(__0);
        }

        /// <summary>
        /// パッチ不可時のフォールバック。Trigger.invokingAction(public event)へ
        /// リフレクション+ジェネリックメソッド閉包でハンドラを差し込む。
        /// </summary>
        private static bool TrySubscribeInvokingAction(Type triggerType)
        {
            try
            {
                if (triggerType == null) return false;
                var integrationType = typeof(Editor).Assembly.GetType(
                    "UnityEditor.ShortcutManagement.ShortcutIntegration");
                object controller = integrationType?
                    .GetProperty("instance", BindingFlags.Public | BindingFlags.Static)?
                    .GetValue(null);
                object trigger = controller?.GetType()
                    .GetProperty("trigger", BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(controller);
                var ev = triggerType.GetEvent("invokingAction",
                    BindingFlags.Public | BindingFlags.Instance);
                if (trigger == null || ev == null) return false;

                var invokeParams = ev.EventHandlerType.GetMethod("Invoke").GetParameters();
                if (invokeParams.Length != 2) return false;
                var mi = typeof(MenuCaptureCollector)
                    .GetMethod(nameof(ShortcutInvoked), BindingFlags.NonPublic | BindingFlags.Static)
                    .MakeGenericMethod(invokeParams[0].ParameterType, invokeParams[1].ParameterType);
                ev.AddEventHandler(trigger, Delegate.CreateDelegate(ev.EventHandlerType, mi));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ShortcutInvoked<TEntry, TArgs>(TEntry entry, TArgs args)
        {
            HandleShortcutEntry(entry);
        }

        private static void HandleShortcutEntry(object entry)
        {
            try
            {
                if (entry == null) return;
                if (entryIdentifierProp == null || !entryIdentifierProp.DeclaringType.IsInstanceOfType(entry))
                    entryIdentifierProp = entry.GetType().GetProperty("identifier",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                object identifier = entryIdentifierProp?.GetValue(entry);
                if (identifier == null) return;
                if (identifierPathProp == null)
                    identifierPathProp = identifier.GetType().GetProperty("path",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                string path = identifierPathProp?.GetValue(identifier) as string;
                if (string.IsNullOrEmpty(path)) return;
                // メニュー項目由来のショートカットは ExecuteMenuItem 側で捕捉済み
                if (path.StartsWith("Main Menu/", StringComparison.Ordinal)) return;
                EmitCmd(path, "shortcut");
            }
            catch { }
        }

        // ---------- 出力 ----------

        private static void EmitCmd(string label, string via)
        {
            try
            {
                if (string.IsNullOrEmpty(label) || !OpLogger.EditsAllowed) return;
                double now = EditorApplication.timeSinceStartup;
                // コンテキストメニュー→ExecuteMenuItem の二重捕捉をデデュープ
                if (label == lastCmd && now - lastCmdT < 0.2) return;
                lastCmd = label;
                lastCmdT = now;
                OpLogger.Emit(OpEvent.New(OpType.Cmd).Str("cmd", label).Str("via", via));
            }
            catch { }
        }
    }
}
#endif // YOZOLAB_OPLOG_HARMONY
