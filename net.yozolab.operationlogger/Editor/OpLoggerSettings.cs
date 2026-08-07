using UnityEditor;
using UnityEngine;

namespace YozoLab.OperationLogger
{
    /// <summary>
    /// プロジェクト単位の設定。
    /// パッケージ内にアセットを置くと VPM 更新で消えるため ProjectSettings/ に保存する
    /// (FBXAnimationExtractorSettings と同じパターン)。
    ///
    /// 録画のマスタースイッチだけは「ユーザー毎」の EditorPrefs(既定 OFF)。
    /// パッケージを入れただけで黙って録画が始まらないようにするため。
    /// </summary>
    [FilePath("ProjectSettings/YozoLabOperationLoggerSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class OpLoggerSettings : ScriptableSingleton<OpLoggerSettings>
    {
        public bool logSelection = true;    // 選択変更(sel)
        public bool logWindowFocus = true;  // ウィンドウフォーカス(win)
        public bool logAssets = true;       // アセット操作(asset)
        public bool logErrors = true;       // Error/Exception(err)
        public bool playModeCapture = false; // プレイ中も編集系イベントを記録するか

        [Range(100, 5000)] public int coalesceWindowMs = 500; // prop 束ねのアイドル窓
        [Range(1, 32)] public int rotationMB = 2;             // ログローテーションの上限

        public void SaveSettings() => Save(true);
    }

    /// <summary>YozoLab/Operation Logger/ メニュー。トグルは Menu.SetChecked で状態表示。</summary>
    internal static class OpLoggerMenu
    {
        private const string Root = "YozoLab/Operation Logger/";
        private const string MenuEnable = Root + "Enable Recording";
        private const string MenuSel = Root + "Capture Selection";
        private const string MenuWin = Root + "Capture Window Focus";
        private const string MenuAssets = Root + "Capture Assets";
        private const string MenuErrors = Root + "Capture Errors";
        private const string MenuPlay = Root + "Capture In Play Mode";

        [MenuItem(MenuEnable, false, 1)]
        private static void ToggleEnable() => OpLogger.SetEnabled(!OpLogger.Enabled);

        [MenuItem(MenuEnable, true)]
        private static bool ValidateEnable()
        {
            Menu.SetChecked(MenuEnable, OpLogger.Enabled);
            return true;
        }

        [MenuItem(Root + "Start New Session", false, 20)]
        private static void StartNewSession()
        {
            if (OpLogger.Enabled) OpLogger.RestartSession();
        }

        [MenuItem(Root + "Start New Session", true)]
        private static bool ValidateStartNewSession() => OpLogger.Enabled;

        [MenuItem(Root + "Open Log Folder", false, 21)]
        private static void OpenLogFolder()
        {
            System.IO.Directory.CreateDirectory(JsonlSink.LogDirectory);
            EditorUtility.RevealInFinder(JsonlSink.LogDirectory);
        }

        // ---------- コレクタ別トグル ----------

        [MenuItem(MenuSel, false, 40)]
        private static void ToggleSel()
        {
            var s = OpLoggerSettings.instance;
            s.logSelection = !s.logSelection;
            s.SaveSettings();
        }

        [MenuItem(MenuSel, true)]
        private static bool VSel()
        {
            Menu.SetChecked(MenuSel, OpLoggerSettings.instance.logSelection);
            return true;
        }

        [MenuItem(MenuWin, false, 41)]
        private static void ToggleWin()
        {
            var s = OpLoggerSettings.instance;
            s.logWindowFocus = !s.logWindowFocus;
            s.SaveSettings();
        }

        [MenuItem(MenuWin, true)]
        private static bool VWin()
        {
            Menu.SetChecked(MenuWin, OpLoggerSettings.instance.logWindowFocus);
            return true;
        }

        [MenuItem(MenuAssets, false, 42)]
        private static void ToggleAssets()
        {
            var s = OpLoggerSettings.instance;
            s.logAssets = !s.logAssets;
            s.SaveSettings();
        }

        [MenuItem(MenuAssets, true)]
        private static bool VAssets()
        {
            Menu.SetChecked(MenuAssets, OpLoggerSettings.instance.logAssets);
            return true;
        }

        [MenuItem(MenuErrors, false, 43)]
        private static void ToggleErrors()
        {
            var s = OpLoggerSettings.instance;
            s.logErrors = !s.logErrors;
            s.SaveSettings();
        }

        [MenuItem(MenuErrors, true)]
        private static bool VErrors()
        {
            Menu.SetChecked(MenuErrors, OpLoggerSettings.instance.logErrors);
            return true;
        }

        [MenuItem(MenuPlay, false, 44)]
        private static void TogglePlay()
        {
            var s = OpLoggerSettings.instance;
            s.playModeCapture = !s.playModeCapture;
            s.SaveSettings();
        }

        [MenuItem(MenuPlay, true)]
        private static bool VPlay()
        {
            Menu.SetChecked(MenuPlay, OpLoggerSettings.instance.playModeCapture);
            return true;
        }
    }
}
