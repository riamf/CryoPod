using System;
using System.Diagnostics;
using CryoPod.Interop;

namespace CryoPod.Services.Launch
{
    internal sealed class ForegroundProcessControlService : IDisposable
    {
        private int? _suspendedProcessId;
        private IntPtr _suspendedProcessHandle;
        private IntPtr _suspendedWindowHandle;
        private bool _isDisposed;

        public bool HasSuspendedProcess => _suspendedProcessId.HasValue;

        public bool ToggleTrackedProcessSuspension(IntPtr appWindowHandle, Process? trackedProcess)
        {
            if (_suspendedProcessId is int suspendedProcessId)
            {
                return TryResumeProcessAndActivateWindow(suspendedProcessId);
            }

            return TrySuspendTrackedProcessAndActivateWindow(appWindowHandle, trackedProcess);
        }

        public void ClearSuspendedProcessIfMatches(int processId)
        {
            if (_suspendedProcessId == processId)
            {
                ClearSuspendedProcessState();
            }
        }

        public bool TryMinimizeTrackedOrForegroundProcessAndActivateWindow(IntPtr appWindowHandle, int appProcessId, Process? trackedProcess)
        {
            var targetWindowHandle = GetTrackedProcessWindowHandle(trackedProcess);
            if (targetWindowHandle != IntPtr.Zero)
            {
                NativeMethods.ShowWindow(targetWindowHandle, NativeMethods.SW_MINIMIZE);
                ActivateWindow(appWindowHandle);
                return true;
            }

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

            NativeMethods.ShowWindow(foregroundWindowHandle, NativeMethods.SW_MINIMIZE);
            ActivateWindow(appWindowHandle);
            return true;
        }

        private bool TrySuspendTrackedProcessAndActivateWindow(IntPtr appWindowHandle, Process? trackedProcess)
        {
            if (trackedProcess is null)
            {
                ActivateWindow(appWindowHandle);
                return false;
            }

            int processId;
            IntPtr windowHandle;

            try
            {
                if (trackedProcess.HasExited)
                {
                    ActivateWindow(appWindowHandle);
                    return false;
                }

                trackedProcess.Refresh();
                processId = trackedProcess.Id;
                windowHandle = trackedProcess.MainWindowHandle;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Failed to inspect tracked game process before suspension: {exception}");
                ActivateWindow(appWindowHandle);
                return false;
            }

            var suspendSucceeded = TrySuspendProcess(processId);
            if (suspendSucceeded)
            {
                _suspendedProcessId = processId;
                _suspendedWindowHandle = windowHandle;
                if (windowHandle != IntPtr.Zero)
                {
                    NativeMethods.ShowWindow(windowHandle, NativeMethods.SW_MINIMIZE);
                }
            }

            ActivateWindow(appWindowHandle);
            return suspendSucceeded;
        }

        private bool TryResumeProcessAndActivateWindow(int processId)
        {
            var resumeSucceeded = TryResumeProcess(processId);
            if (!resumeSucceeded)
            {
                ClearSuspendedProcessState();
                return false;
            }

            var suspendedWindowHandle = _suspendedWindowHandle;
            ClearSuspendedProcessState();
            ActivateWindow(suspendedWindowHandle);
            return true;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            if (_suspendedProcessId is int suspendedProcessId)
            {
                if (!TryResumeProcess(suspendedProcessId))
                {
                    Debug.WriteLine($"Failed to resume suspended process {suspendedProcessId} during cleanup.");
                }

                ClearSuspendedProcessState();
            }
        }

        private bool TrySuspendProcess(int processId)
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
                    _suspendedProcessHandle = processHandle;
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
                if (_suspendedProcessHandle != processHandle)
                {
                    NativeMethods.CloseHandle(processHandle);
                }
            }
        }

        private bool TryResumeProcess(int processId)
        {
            var processHandle = _suspendedProcessHandle;
            var ownsHandle = false;

            if (processHandle == IntPtr.Zero)
            {
                processHandle = NativeMethods.OpenProcess(NativeMethods.PROCESS_SUSPEND_RESUME, false, (uint)processId);
                if (processHandle == IntPtr.Zero)
                {
                    Debug.WriteLine($"Failed to open suspended process {processId} for resume.");
                    return false;
                }

                ownsHandle = true;
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
                if (ownsHandle)
                {
                    NativeMethods.CloseHandle(processHandle);
                }
            }
        }

        private void ClearSuspendedProcessState()
        {
            if (_suspendedProcessHandle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(_suspendedProcessHandle);
                _suspendedProcessHandle = IntPtr.Zero;
            }

            _suspendedProcessId = null;
            _suspendedWindowHandle = IntPtr.Zero;
        }

        private static IntPtr GetTrackedProcessWindowHandle(Process? trackedProcess)
        {
            if (trackedProcess is null)
            {
                return IntPtr.Zero;
            }

            try
            {
                if (trackedProcess.HasExited)
                {
                    return IntPtr.Zero;
                }

                trackedProcess.Refresh();
                return trackedProcess.MainWindowHandle;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Failed to inspect tracked game window handle: {exception}");
                return IntPtr.Zero;
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
