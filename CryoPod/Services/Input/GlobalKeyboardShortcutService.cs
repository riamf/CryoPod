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

        public GlobalKeyboardShortcutService(IntPtr windowHandle)
        {
            _windowHandle = windowHandle;
            _wndProcDelegate = HotkeyWndProc;
            _wndProcPointer = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);

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
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                NativeMethods.VK_F2);

            if (!_isHotkeyRegistered)
            {
                Debug.WriteLine("Failed to register the global hotkey. The shortcut may already be in use.");
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
    }
}
