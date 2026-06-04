using System;
using System.Diagnostics;
using CryoPod.Interop;

namespace CryoPod.Services.Launch
{
    internal sealed class ForegroundProcessControlService
    {
        private int? _suspendedProcessId;
        private IntPtr _suspendedWindowHandle;

        public bool ToggleForegroundProcessSuspension(IntPtr appWindowHandle, int appProcessId)
        {
            if (_suspendedProcessId is int suspendedProcessId)
            {
                return TryResumeProcessAndActivateWindow(suspendedProcessId);
            }

            return TrySuspendForegroundProcessAndActivateWindow(appWindowHandle, appProcessId);
        }

        private bool TrySuspendForegroundProcessAndActivateWindow(IntPtr appWindowHandle, int appProcessId)
        {
            var foregroundWindowHandle = NativeMethods.GetForegroundWindow();
            if (foregroundWindowHandle == IntPtr.Zero || foregroundWindowHandle == appWindowHandle)
            {
                ActivateWindow(appWindowHandle);
                return false;
            }

            _ = NativeMethods.GetWindowThreadProcessId(foregroundWindowHandle, out var processId);
            if (processId == 0 || processId == appProcessId)
            {
                ActivateWindow(appWindowHandle);
                return false;
            }

            var suspendSucceeded = TrySuspendProcess((int)processId);
            if (suspendSucceeded)
            {
                _suspendedProcessId = (int)processId;
                _suspendedWindowHandle = foregroundWindowHandle;
                NativeMethods.ShowWindow(foregroundWindowHandle, NativeMethods.SW_MINIMIZE);
            }

            ActivateWindow(appWindowHandle);
            return suspendSucceeded;
        }

        private bool TryResumeProcessAndActivateWindow(int processId)
        {
            var resumeSucceeded = TryResumeProcess(processId);
            if (!resumeSucceeded)
            {
                _suspendedProcessId = null;
                _suspendedWindowHandle = IntPtr.Zero;
                return false;
            }

            var suspendedWindowHandle = _suspendedWindowHandle;
            _suspendedProcessId = null;
            _suspendedWindowHandle = IntPtr.Zero;
            ActivateWindow(suspendedWindowHandle);
            return true;
        }

        private static bool TrySuspendProcess(int processId)
        {
            var processHandle = NativeMethods.OpenProcess(NativeMethods.PROCESS_SUSPEND_RESUME, false, (uint)processId);
            if (processHandle == IntPtr.Zero)
            {
                Debug.WriteLine($"Failed to open foreground process {processId} for suspension.");
                return false;
            }

            try
            {
                var status = NativeMethods.NtSuspendProcess(processHandle);
                if (status == 0)
                {
                    return true;
                }

                Debug.WriteLine($"NtSuspendProcess failed for process {processId} with status 0x{status:X8}.");
                return false;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Failed to suspend foreground process {processId}: {exception}");
                return false;
            }
            finally
            {
                NativeMethods.CloseHandle(processHandle);
            }
        }

        private static bool TryResumeProcess(int processId)
        {
            var processHandle = NativeMethods.OpenProcess(NativeMethods.PROCESS_SUSPEND_RESUME, false, (uint)processId);
            if (processHandle == IntPtr.Zero)
            {
                Debug.WriteLine($"Failed to open suspended process {processId} for resume.");
                return false;
            }

            try
            {
                var status = NativeMethods.NtResumeProcess(processHandle);
                if (status == 0)
                {
                    return true;
                }

                Debug.WriteLine($"NtResumeProcess failed for process {processId} with status 0x{status:X8}.");
                return false;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Failed to resume suspended process {processId}: {exception}");
                return false;
            }
            finally
            {
                NativeMethods.CloseHandle(processHandle);
            }
        }

        private static void ActivateWindow(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.ShowWindow(windowHandle, NativeMethods.SW_RESTORE);
            NativeMethods.BringWindowToTop(windowHandle);
            NativeMethods.SetForegroundWindow(windowHandle);
        }
    }
}
