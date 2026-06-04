using System;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CryoPod.Interop;

namespace CryoPod.Services.Input
{
    internal sealed class GlobalKeyboardShortcutService : IDisposable
    {
        private readonly IntPtr _windowHandle;
        private readonly NativeMethods.WndProcDelegate _wndProcDelegate;
        private readonly IntPtr _wndProcPointer;
        private IntPtr _previousWndProc;
        private bool _isDisposed;
        private bool _isHotkeyRegistered;
        private IntPtr _hookHandle;
        private readonly NativeMethods.LowLevelKeyboardProc _hookProc;
        private bool _isCtrlPressed;
        private bool _isShiftPressed;
        private bool _shortcutHandled;

        public GlobalKeyboardShortcutService(IntPtr windowHandle)
        {
            _windowHandle = windowHandle;
            _wndProcDelegate = HotkeyWndProc;
            _wndProcPointer = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
            _hookProc = LowLevelKeyboardHookCallback;

            if (_windowHandle == IntPtr.Zero)
            {
                Debug.WriteLine("Global hotkey registration skipped because the window handle was unavailable.");
                return;
            }

            _previousWndProc = NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GWLP_WNDPROC, _wndProcPointer);
            if (_previousWndProc == IntPtr.Zero)
            {
                Debug.WriteLine("Failed to subclass the window procedure for hotkey handling.");
                return;
            }

            _isHotkeyRegistered = NativeMethods.RegisterHotKey(
                _windowHandle,
                NativeMethods.HOTKEY_ID,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT,
                NativeMethods.VK_F2);

            if (_isHotkeyRegistered)
            {
                Debug.WriteLine("Global hotkey registered successfully using RegisterHotKey.");
            }
            else
            {
                Debug.WriteLine("RegisterHotKey failed. Falling back to low-level keyboard hook.");
            }

            _hookHandle = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL,
                _hookProc,
                IntPtr.Zero,
                0);

            if (_hookHandle == IntPtr.Zero)
            {
                Debug.WriteLine("Failed to install low-level keyboard hook.");
            }
            else
            {
                Debug.WriteLine("Low-level keyboard hook installed successfully.");
            }
        }

        public event EventHandler? ShortcutPressed;

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            if (_hookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }

            if (_windowHandle != IntPtr.Zero)
            {
                if (_isHotkeyRegistered)
                {
                    NativeMethods.UnregisterHotKey(_windowHandle, NativeMethods.HOTKEY_ID);
                    _isHotkeyRegistered = false;
                }

                if (_previousWndProc != IntPtr.Zero)
                {
                    NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GWLP_WNDPROC, _previousWndProc);
                    _previousWndProc = IntPtr.Zero;
                }
            }
        }

        private IntPtr HotkeyWndProc(
            IntPtr hWnd,
            uint msg,
            IntPtr wParam,
            IntPtr lParam)
        {
            if (msg == NativeMethods.WM_HOTKEY && wParam == (IntPtr)NativeMethods.HOTKEY_ID)
            {
                ShortcutPressed?.Invoke(this, EventArgs.Empty);
                return IntPtr.Zero;
            }

            return NativeMethods.CallWindowProc(_previousWndProc, hWnd, msg, wParam, lParam);
        }

        private IntPtr LowLevelKeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                bool isKeyDown = wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN;

                if (hookStruct.vkCode == 0x10)
                {
                    _isShiftPressed = isKeyDown;
                    _shortcutHandled = false;
                }
                else if (hookStruct.vkCode == 0x11)
                {
                    _isCtrlPressed = isKeyDown;
                    _shortcutHandled = false;
                }
                else if (hookStruct.vkCode == NativeMethods.VK_F2 && isKeyDown)
                {
                    if (_isCtrlPressed && _isShiftPressed && !_shortcutHandled)
                    {
                        _shortcutHandled = true;
                        ShortcutPressed?.Invoke(this, EventArgs.Empty);
                    }
                }
            }

            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }
    }
}
