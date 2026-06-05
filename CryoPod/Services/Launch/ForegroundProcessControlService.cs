using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CryoPod.Interop;

namespace CryoPod.Services.Launch
{
    internal sealed class SuspendedProcessState
    {
        public SuspendedProcessState(IReadOnlyList<int> processIds, IReadOnlyList<IntPtr> processHandles)
        {
            ProcessIds = processIds;
            ProcessHandles = processHandles;
        }

        public IReadOnlyList<int> ProcessIds { get; }

        public IReadOnlyList<IntPtr> ProcessHandles { get; }

        public bool HasHandles => ProcessHandles.Count > 0;
    }

    internal sealed class ForegroundProcessControlService
    {
        public bool TrySuspendTrackedProcessAndActivateWindow(IntPtr appWindowHandle, Process? trackedProcess, out SuspendedProcessState? suspendedState)
        {
            suspendedState = null;
            Debug.WriteLine(trackedProcess is null
                ? "TrySuspendTrackedProcessAndActivateWindow called without a tracked process."
                : $"TrySuspendTrackedProcessAndActivateWindow called for process {trackedProcess.Id}.");
            return TrySuspendTrackedProcessAndActivateWindowCore(appWindowHandle, trackedProcess, ref suspendedState);
        }

        public bool TryResumeSuspendedProcessAndActivateWindow(Process? trackedProcess, SuspendedProcessState? suspendedState)
        {
            if (suspendedState is null || !suspendedState.HasHandles)
            {
                ActivateWindow(GetTrackedProcessWindowHandle(trackedProcess));
                return true;
            }

            return TryResumeProcessAndActivateWindow(trackedProcess, suspendedState);
        }

        public bool TryResumeSuspendedProcess(SuspendedProcessState? suspendedState)
        {
            if (suspendedState is null || !suspendedState.HasHandles)
            {
                return true;
            }

            return TryResumeProcesses(suspendedState);
        }

        public bool TryMinimizeTrackedOrForegroundProcessAndActivateWindow(IntPtr appWindowHandle, int appProcessId, Process? trackedProcess)
        {
            var targetWindowHandle = GetTrackedProcessWindowHandle(trackedProcess);
            if (targetWindowHandle != IntPtr.Zero)
            {
                Debug.WriteLine($"Minimizing tracked process window 0x{targetWindowHandle.ToInt64():X}.");
                NativeMethods.ShowWindow(targetWindowHandle, NativeMethods.SW_MINIMIZE);
                ActivateWindow(appWindowHandle);
                return true;
            }

            var foregroundWindowHandle = NativeMethods.GetForegroundWindow();
            if (foregroundWindowHandle == IntPtr.Zero || foregroundWindowHandle == appWindowHandle)
            {
                Debug.WriteLine($"Could not minimize foreground window. ForegroundHandle=0x{foregroundWindowHandle.ToInt64():X}, AppHandle=0x{appWindowHandle.ToInt64():X}.");
                ActivateWindow(appWindowHandle);
                return false;
            }

            _ = NativeMethods.GetWindowThreadProcessId(foregroundWindowHandle, out var processId);
            if (processId == 0 || processId == appProcessId)
            {
                Debug.WriteLine($"Could not minimize foreground window because resolved process id was {processId} and app process id is {appProcessId}.");
                ActivateWindow(appWindowHandle);
                return false;
            }

            Debug.WriteLine($"Minimizing foreground window 0x{foregroundWindowHandle.ToInt64():X} for process {processId}.");
            NativeMethods.ShowWindow(foregroundWindowHandle, NativeMethods.SW_MINIMIZE);
            ActivateWindow(appWindowHandle);
            return true;
        }

        public void ResumeSuspendedProcessForCleanup(SuspendedProcessState? suspendedState)
        {
            if (suspendedState is null || !suspendedState.HasHandles)
            {
                return;
            }

            try
            {
                _ = TryResumeProcesses(suspendedState);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Failed to resume suspended process during cleanup: {exception}");
            }
            finally
            {
                CloseProcessHandles(suspendedState);
            }
        }

        public void CloseSuspendedProcessHandles(SuspendedProcessState? suspendedState)
        {
            if (suspendedState is null || !suspendedState.HasHandles)
            {
                return;
            }

            CloseProcessHandles(suspendedState);
        }

        private bool TrySuspendTrackedProcessAndActivateWindowCore(IntPtr appWindowHandle, Process? trackedProcess, ref SuspendedProcessState? suspendedState)
        {
            if (trackedProcess is null)
            {
                Debug.WriteLine("TrySuspendTrackedProcessAndActivateWindowCore aborted because tracked process is null.");
                ActivateWindow(appWindowHandle);
                return false;
            }

            int processId;
            IntPtr windowHandle;

            try
            {
                if (trackedProcess.HasExited)
                {
                    Debug.WriteLine("TrySuspendTrackedProcessAndActivateWindowCore aborted because tracked process has exited.");
                    ActivateWindow(appWindowHandle);
                    return false;
                }

                trackedProcess.Refresh();
                processId = trackedProcess.Id;
                windowHandle = trackedProcess.MainWindowHandle;
                Debug.WriteLine($"Preparing to suspend process {processId}. MainWindowHandle=0x{windowHandle.ToInt64():X}.");
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Failed to inspect tracked game process before suspension: {exception}");
                ActivateWindow(appWindowHandle);
                return false;
            }

            var suspendSucceeded = TrySuspendProcessTree(processId, out suspendedState);
            if (suspendSucceeded)
            {
                if (windowHandle != IntPtr.Zero)
                {
                    Debug.WriteLine($"Suspension succeeded for process {processId}. Minimizing window 0x{windowHandle.ToInt64():X}.");
                    NativeMethods.ShowWindow(windowHandle, NativeMethods.SW_MINIMIZE);
                }

                Debug.WriteLine($"Suspension succeeded for process {processId}. SuspendedProcesses=[{string.Join(", ", suspendedState?.ProcessIds ?? [])}].");
            }
            else
            {
                Debug.WriteLine($"Suspension failed for process {processId}.");
            }

            ActivateWindow(appWindowHandle);
            return suspendSucceeded;
        }

        private bool TryResumeProcessAndActivateWindow(Process? trackedProcess, SuspendedProcessState suspendedState)
        {
            var resumeSucceeded = TryResumeProcesses(suspendedState);
            if (!resumeSucceeded)
            {
                return false;
            }

            ActivateWindow(GetTrackedProcessWindowHandle(trackedProcess));
            return true;
        }

        private static bool TrySuspendProcessTree(int processId, out SuspendedProcessState? suspendedState)
        {
            suspendedState = null;
            var processIds = GetProcessTree(processId);
            Debug.WriteLine($"Resolved suspend process tree for root {processId}: [{string.Join(", ", processIds)}].");

            var suspendedProcessIds = new List<int>(processIds.Count);
            var suspendedHandles = new List<IntPtr>(processIds.Count);

            foreach (var currentProcessId in processIds)
            {
                if (!TrySuspendSingleProcess(currentProcessId, out var processHandle))
                {
                    Debug.WriteLine($"Failed to suspend process tree rooted at {processId}. Rolling back {suspendedProcessIds.Count} suspended process(es).");
                    TryResumeProcesses(new SuspendedProcessState(suspendedProcessIds, suspendedHandles));
                    CloseProcessHandles(new SuspendedProcessState(suspendedProcessIds, suspendedHandles));
                    return false;
                }

                suspendedProcessIds.Add(currentProcessId);
                suspendedHandles.Add(processHandle);
            }

            suspendedState = new SuspendedProcessState(suspendedProcessIds, suspendedHandles);
            return true;
        }

        private static bool TrySuspendSingleProcess(int processId, out IntPtr suspendedHandle)
        {
            suspendedHandle = IntPtr.Zero;
            var processHandle = NativeMethods.OpenProcess(
                NativeMethods.PROCESS_SUSPEND_RESUME | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
                false,
                (uint)processId);
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
                    Debug.WriteLine($"NtSuspendProcess succeeded for process {processId}.");
                    suspendedHandle = processHandle;
                    return true;
                }

                Debug.WriteLine($"NtSuspendProcess failed for process {processId} with status 0x{status:X8}. Attempting thread-level fallback.");
                if (TrySetProcessThreadsSuspended(processId, suspend: true))
                {
                    Debug.WriteLine($"Thread-level suspend fallback succeeded for process {processId}.");
                    suspendedHandle = processHandle;
                    return true;
                }

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

        private static bool TryResumeProcesses(SuspendedProcessState suspendedState)
        {
            var allSucceeded = true;

            for (var index = suspendedState.ProcessHandles.Count - 1; index >= 0; index--)
            {
                var processHandle = suspendedState.ProcessHandles[index];
                var processId = suspendedState.ProcessIds[index];
                if (TryResumeSingleProcess(processHandle, processId))
                {
                    continue;
                }

                allSucceeded = false;
            }

            return allSucceeded;
        }

        private static bool TryResumeSingleProcess(IntPtr suspendedHandle, int expectedProcessId)
        {
            try
            {
                var status = NativeMethods.NtResumeProcess(suspendedHandle);
                if (status == 0)
                {
                    Debug.WriteLine($"NtResumeProcess succeeded for process {expectedProcessId}.");
                    return true;
                }

                var processId = NativeMethods.GetProcessId(suspendedHandle);
                if (processId != 0)
                {
                    Debug.WriteLine($"NtResumeProcess failed with status 0x{status:X8}. Attempting thread-level fallback for process {processId}.");
                    if (TrySetProcessThreadsSuspended((int)processId, suspend: false))
                    {
                        Debug.WriteLine($"Thread-level resume fallback succeeded for process {processId}.");
                        return true;
                    }
                }

                Debug.WriteLine($"NtResumeProcess failed with status 0x{status:X8} for process {expectedProcessId}.");
                return false;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Failed to resume suspended process {expectedProcessId}: {exception}");
                return false;
            }
        }

        private static List<int> GetProcessTree(int rootProcessId)
        {
            var snapshotHandle = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
            if (snapshotHandle == NativeMethods.INVALID_HANDLE_VALUE)
            {
                Debug.WriteLine($"Failed to capture process snapshot for root process {rootProcessId}. Win32 error: {Marshal.GetLastWin32Error()}.");
                return [rootProcessId];
            }

            try
            {
                var entry = new NativeMethods.PROCESSENTRY32
                {
                    dwSize = (uint)Marshal.SizeOf<NativeMethods.PROCESSENTRY32>()
                };

                if (!NativeMethods.Process32First(snapshotHandle, ref entry))
                {
                    Debug.WriteLine($"Failed to enumerate first process for root process {rootProcessId}. Win32 error: {Marshal.GetLastWin32Error()}.");
                    return [rootProcessId];
                }

                var childProcessIdsByParentId = new Dictionary<int, List<int>>();

                do
                {
                    var parentProcessId = (int)entry.th32ParentProcessID;
                    var childProcessId = (int)entry.th32ProcessID;

                    if (!childProcessIdsByParentId.TryGetValue(parentProcessId, out var childProcessIds))
                    {
                        childProcessIds = [];
                        childProcessIdsByParentId[parentProcessId] = childProcessIds;
                    }

                    childProcessIds.Add(childProcessId);
                }
                while (NativeMethods.Process32Next(snapshotHandle, ref entry));

                var processTree = new List<int>();
                var visitedProcessIds = new HashSet<int>();
                var pendingProcessIds = new Queue<int>();
                pendingProcessIds.Enqueue(rootProcessId);

                while (pendingProcessIds.Count > 0)
                {
                    var currentProcessId = pendingProcessIds.Dequeue();
                    if (!visitedProcessIds.Add(currentProcessId))
                    {
                        continue;
                    }

                    processTree.Add(currentProcessId);

                    if (!childProcessIdsByParentId.TryGetValue(currentProcessId, out var childProcessIds))
                    {
                        continue;
                    }

                    foreach (var childProcessId in childProcessIds)
                    {
                        pendingProcessIds.Enqueue(childProcessId);
                    }
                }

                return processTree;
            }
            finally
            {
                NativeMethods.CloseHandle(snapshotHandle);
            }
        }

        private static void CloseProcessHandles(SuspendedProcessState suspendedState)
        {
            foreach (var processHandle in suspendedState.ProcessHandles)
            {
                if (processHandle != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(processHandle);
                }
            }
        }

        private static bool TrySetProcessThreadsSuspended(int processId, bool suspend)
        {
            var snapshotHandle = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPTHREAD, 0);
            if (snapshotHandle == NativeMethods.INVALID_HANDLE_VALUE)
            {
                Debug.WriteLine($"Failed to capture thread snapshot for process {processId}. Win32 error: {Marshal.GetLastWin32Error()}.");
                return false;
            }

            try
            {
                var entry = new NativeMethods.THREADENTRY32
                {
                    dwSize = (uint)Marshal.SizeOf<NativeMethods.THREADENTRY32>()
                };

                if (!NativeMethods.Thread32First(snapshotHandle, ref entry))
                {
                    Debug.WriteLine($"Failed to enumerate first thread for process {processId}. Win32 error: {Marshal.GetLastWin32Error()}.");
                    return false;
                }

                var attempted = 0;
                var failed = 0;

                do
                {
                    if (entry.th32OwnerProcessID != (uint)processId)
                    {
                        continue;
                    }

                    attempted++;
                    var threadHandle = NativeMethods.OpenThread(NativeMethods.THREAD_SUSPEND_RESUME, false, entry.th32ThreadID);
                    if (threadHandle == IntPtr.Zero)
                    {
                        failed++;
                        continue;
                    }

                    try
                    {
                        var result = suspend
                            ? NativeMethods.SuspendThread(threadHandle)
                            : NativeMethods.ResumeThread(threadHandle);

                        if (result == uint.MaxValue)
                        {
                            failed++;
                        }
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(threadHandle);
                    }
                }
                while (NativeMethods.Thread32Next(snapshotHandle, ref entry));

                if (attempted == 0)
                {
                    Debug.WriteLine($"No threads were found for process {processId} during {(suspend ? "suspend" : "resume")} fallback.");
                    return false;
                }

                if (failed > 0)
                {
                    Debug.WriteLine($"Thread-level {(suspend ? "suspend" : "resume")} fallback partially failed for process {processId}: {failed}/{attempted} thread operations failed.");
                    return false;
                }

                return true;
            }
            finally
            {
                NativeMethods.CloseHandle(snapshotHandle);
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
