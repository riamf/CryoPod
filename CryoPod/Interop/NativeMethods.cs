using System;
using System.Runtime.InteropServices;

namespace CryoPod.Interop
{
    internal static class NativeMethods
    {
        internal const int SW_HIDE = 0;
        internal const int SW_MINIMIZE = 6;
        internal const int SW_RESTORE = 9;
        internal const int SW_SHOW = 5;
        internal const int GWLP_WNDPROC = -4;
        internal const uint WM_HOTKEY = 0x0312;
        internal const uint MOD_ALT = 0x0001;
        internal const uint MOD_CONTROL = 0x0002;
        internal const uint MOD_SHIFT = 0x0004;
        internal const uint MOD_NOREPEAT = 0x4000;
        internal const uint VK_HOME = 0x24;
        internal const uint VK_F2 = 0x71;
        internal const uint VK_F12 = 0x7B;
        internal const uint PROCESS_SUSPEND_RESUME = 0x0800;
        internal const int HOTKEY_ID = 0xC001;
        internal const int WH_KEYBOARD_LL = 13;
        internal const int WM_KEYDOWN = 0x0100;
        internal const int WM_SYSKEYDOWN = 0x0104;

        internal delegate IntPtr WndProcDelegate(
            IntPtr hWnd,
            uint msg,
            IntPtr wParam,
            IntPtr lParam);

        internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        internal struct KBDLLHOOKSTRUCT
        {
            internal uint vkCode;
            internal uint scanCode;
            internal uint flags;
            internal uint time;
            internal IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
        internal static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr hObject);

        [DllImport("ntdll.dll")]
        internal static extern uint NtSuspendProcess(IntPtr hProcess);

        [DllImport("ntdll.dll")]
        internal static extern uint NtResumeProcess(IntPtr hProcess);
    }
}
