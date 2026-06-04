using System;
using System.Diagnostics;
using CryoPod.Interop;

namespace CryoPod.Services.Launch
{
    internal sealed class ForegroundProcessControlService
    {
        public bool TrySuspendTrackedProcessAndActivateWindow(IntPtr appWindowHandle, Process? trackedProcess, out IntPtr suspendedHandle)
        {
            suspendedHandle = IntPtr.Zero;
            return TrySuspendTrackedProcessAndActivateWindowCore(appWindowHandle, trackedProcess, ref suspendedHandle);
        }

        public bool TryResumeSuspendedProcessAndActivateWindow(Process? trackedProcess, IntPtr suspendedHandle)
        {
            if (suspendedHandle == IntPtr.Zero)
            {
                ActivateWindow(GetTrackedProcessWindowHandle(trackedProcess));
                return true;
            }

            return TryResumeProcessAndActivateWindow(trackedProcess, suspendedHandle);
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

        public void ResumeSuspendedProcessForCleanup(IntPtr suspendedHandle)
        {
            if (suspendedHandle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                _ = NativeMethods.NtResumeProcess(suspendedHandle);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Failed to resume suspended process during cleanup: {exception}");
            }
            finally
            {
                NativeMethods.CloseHandle(suspendedHandle);
            }
        }

        private bool TrySuspendTrackedProcessAndActivateWindowCore(IntPtr appWindowHandle, Process? trackedProcess, ref IntPtr suspendedHandle)
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

            var suspendSucceeded = TrySuspendProcess(processId, ref suspendedHandle);
            if (suspendSucceeded)
            {
                if (windowHandle != IntPtr.Zero)
                {
                    NativeMethods.ShowWindow(windowHandle, NativeMethods.SW_MINIMIZE);
                }
            }

            ActivateWindow(appWindowHandle);
            return suspendSucceeded;
        }

        private bool TryResumeProcessAndActivateWindow(Process? trackedProcess, IntPtr suspendedHandle)
        {
            var resumeSucceeded = TryResumeProcess(suspendedHandle);
            if (!resumeSucceeded)
            {
                return false;
            }

            ActivateWindow(GetTrackedProcessWindowHandle(trackedProcess));
            return true;
        }

        private static bool TrySuspendProcess(int processId, ref IntPtr suspendedHandle)
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
                    suspendedHandle = processHandle;
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
                if (suspendedHandle != processHandle)
                {
                    NativeMethods.CloseHandle(processHandle);
                }
            }
        }

        private static bool TryResumeProcess(IntPtr suspendedHandle)
        {
            try
            {
                var status = NativeMethods.NtResumeProcess(suspendedHandle);
                if (status == 0)
                {
                    return true;
                }

                Debug.WriteLine($"NtResumeProcess failed with status 0x{status:X8}.");
                return false;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Failed to resume suspended process: {exception}");
                return false;
            }
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
