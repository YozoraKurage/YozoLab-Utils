// Windows が描くタイトルバーと各種メニュー（右クリック含む）を暗くする。
//
// Unity 側の配色を変えても、OS が描く部分は明るいままになる。ここを暗くするのに
// 第三者のバイナリは要らない。Windows 自身の API を 2 つ叩くだけで済む。
//
//   uxtheme.dll の SetPreferredAppMode(ForceDark) … 以後に作られるメニューを暗くする
//   uxtheme.dll の FlushMenuThemes()              … 既に作られたメニューを引き直させる
//   dwmapi.dll の DwmSetWindowAttribute(...)      … タイトルバーを暗くする
//
// uxtheme の 2 つは公開名を持たず序数でしか引けない。DllImport の EntryPoint = "#135" は
// 実機で EntryPointNotFoundException になったので、GetProcAddress に序数をそのまま渡す。
//
// 以前はこの役目を 0x7c13/UnityEditor-DarkMode の DLL に任せていたが、他人のビルド済み
// バイナリをパッケージで再配布することになるうえ、一度読み込むと外せなかったのでやめた。
// 自前で呼ぶ形なら同梱物はゼロで、無効化したときに元へ戻せる。
#if UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace YozoLab.EditorTheme
{
    internal static class WindowsChromePatch
    {
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryExW(string path, IntPtr reserved, uint flags);

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);

        [DllImport("kernel32")]
        private static extern uint GetCurrentProcessId();

        [DllImport("user32")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

        [DllImport("user32")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("dwmapi")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr param);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetPreferredAppModeFn(int mode);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void FlushMenuThemesFn();

        private const uint LoadLibrarySearchSystem32 = 0x00000800;
        private const int PreferredAppModeDefault = 0;
        private const int PreferredAppModeForceDark = 2;

        // 20 が正式。Windows 10 の古いビルドでは 19 だったので両方に投げる。
        private const int UseImmersiveDarkMode = 20;
        private const int UseImmersiveDarkModeOld = 19;

        private static bool applied;
        private static string note;

        public static bool IsAvailable => true;
        public static bool IsApplied => applied;

        /// <summary>直近の結果。診断用。</summary>
        public static string Note => note;

        public static void Apply(bool dark)
        {
            if (!SetMenuMode(dark ? PreferredAppModeForceDark : PreferredAppModeDefault)) return;
            SetTitleBars(dark);
            applied = dark;
            note = dark ? "適用済み" : "既定に戻した";
        }

        /// <summary>OS 側の見た目を元に戻す。同梱 DLL と違い、これは即座に効く。</summary>
        public static void Restore()
        {
            if (!applied) return;
            SetMenuMode(PreferredAppModeDefault);
            SetTitleBars(false);
            applied = false;
            note = "戻した";
        }

        private static bool SetMenuMode(int mode)
        {
            IntPtr uxtheme = LoadLibraryExW("uxtheme.dll", IntPtr.Zero, LoadLibrarySearchSystem32);
            if (uxtheme == IntPtr.Zero)
            {
                note = $"uxtheme.dll を読み込めません (Win32 error {Marshal.GetLastWin32Error()})。";
                return false;
            }

            IntPtr setMode = GetProcAddress(uxtheme, new IntPtr(135));
            IntPtr flush = GetProcAddress(uxtheme, new IntPtr(136));
            if (setMode == IntPtr.Zero || flush == IntPtr.Zero)
            {
                note = $"uxtheme の序数が見つかりません (135={setMode != IntPtr.Zero}, 136={flush != IntPtr.Zero})。"
                       + "この Windows ではメニューの配色を変えられません。";
                return false;
            }

            try
            {
                Marshal.GetDelegateForFunctionPointer<SetPreferredAppModeFn>(setMode)(mode);
                Marshal.GetDelegateForFunctionPointer<FlushMenuThemesFn>(flush)();
                return true;
            }
            catch (Exception e)
            {
                note = $"メニューの引き直しに失敗: {e.GetType().Name} {e.Message}";
                return false;
            }
        }

        /// <summary>このプロセスが持つトップレベルウィンドウのタイトルバーを切り替える。</summary>
        private static void SetTitleBars(bool dark)
        {
            var windows = new List<IntPtr>();
            uint self = GetCurrentProcessId();
            try
            {
                EnumWindows((window, _) =>
                {
                    GetWindowThreadProcessId(window, out uint owner);
                    if (owner == self) windows.Add(window);
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception) { return; }

            int value = dark ? 1 : 0;
            foreach (IntPtr window in windows)
            {
                try
                {
                    if (DwmSetWindowAttribute(window, UseImmersiveDarkMode, ref value, sizeof(int)) != 0)
                        DwmSetWindowAttribute(window, UseImmersiveDarkModeOld, ref value, sizeof(int));
                }
                catch (Exception) { /* 古い Windows では単に効かない */ }
            }
        }
    }
}
#else
namespace YozoLab.EditorTheme
{
    /// <summary>Windows 以外では何もしない。</summary>
    internal static class WindowsChromePatch
    {
        public static bool IsAvailable => false;
        public static bool IsApplied => false;
        public static string Note => null;
        public static void Apply(bool dark) { }
        public static void Restore() { }
    }
}
#endif
