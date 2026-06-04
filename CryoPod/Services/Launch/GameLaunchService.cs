using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CryoPod.Models;
using Windows.System;

namespace CryoPod.Services.Launch
{
    internal sealed class GameLaunchService
    {
        private static readonly TimeSpan ProcessDetectionTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ProcessPollingInterval = TimeSpan.FromMilliseconds(500);
        private static readonly Regex NonProcessNameCharacterRegex = new("[^A-Za-z0-9_]", RegexOptions.Compiled);
        private static readonly IReadOnlyDictionary<int, string[]> SteamProcessNamesByAppId = new Dictionary<int, string[]>
        {
        };

        public async Task<(bool LaunchSucceeded, Process? GameProcess)> LaunchAsync(InstalledGame installedGame, CancellationToken cancellationToken = default)
        {
            if (TryLaunchDirectExecutable(installedGame, out var directProcess))
            {
                return (true, directProcess);
            }

            if (installedGame.AppId is not > 0)
            {
                return (false, null);
            }

            var launchUri = new Uri($"steam://rungameid/{installedGame.AppId.Value}");
            var candidateProcessNames = GetCandidateProcessNames(installedGame);
            var knownProcessIds = CaptureExistingProcessIds(candidateProcessNames);
            var launchSucceeded = false;

            try
            {
                if (await Launcher.LaunchUriAsync(launchUri))
                {
                    launchSucceeded = true;
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Failed to launch Steam URI for {installedGame.Name} ({installedGame.AppId}): {exception}");
            }

            if (!launchSucceeded)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = launchUri.ToString(),
                        UseShellExecute = true,
                    });
                    launchSucceeded = true;
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"Failed to start Steam shell launch for {installedGame.Name} ({installedGame.AppId}): {exception}");
                    return (false, null);
                }
            }

            var gameProcess = await WaitForGameProcessAsync(candidateProcessNames, knownProcessIds, cancellationToken);
            return (true, gameProcess);
        }

        private static bool TryLaunchDirectExecutable(InstalledGame installedGame, out Process? process)
        {
            process = null;

            if (string.IsNullOrWhiteSpace(installedGame.InstallPath)
                || !File.Exists(installedGame.InstallPath)
                || !string.Equals(Path.GetExtension(installedGame.InstallPath), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = installedGame.InstallPath,
                    WorkingDirectory = Path.GetDirectoryName(installedGame.InstallPath),
                    UseShellExecute = true,
                });
                return process is not null;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Failed to launch executable for {installedGame.Name}: {exception}");
                return false;
            }
        }

        private static HashSet<string> GetCandidateProcessNames(InstalledGame installedGame)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (installedGame.AppId is int appId && SteamProcessNamesByAppId.TryGetValue(appId, out var mappedProcessNames))
            {
                foreach (var mappedProcessName in mappedProcessNames)
                {
                    AddCandidateProcessName(candidates, mappedProcessName);
                }
            }

            AddCandidateProcessName(candidates, installedGame.Name);

            if (!string.IsNullOrWhiteSpace(installedGame.InstallPath))
            {
                if (File.Exists(installedGame.InstallPath))
                {
                    AddCandidateProcessName(candidates, Path.GetFileNameWithoutExtension(installedGame.InstallPath));
                }
                else if (Directory.Exists(installedGame.InstallPath))
                {
                    foreach (var executablePath in Directory.EnumerateFiles(installedGame.InstallPath, "*.exe", SearchOption.AllDirectories).Take(64))
                    {
                        AddCandidateProcessName(candidates, Path.GetFileNameWithoutExtension(executablePath));
                    }

                    AddCandidateProcessName(candidates, Path.GetFileName(installedGame.InstallPath));
                }
            }

            return candidates;
        }

        private static void AddCandidateProcessName(HashSet<string> candidates, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var trimmedValue = value.Trim();
            if (trimmedValue.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                trimmedValue = Path.GetFileNameWithoutExtension(trimmedValue);
            }

            var normalizedValue = NonProcessNameCharacterRegex.Replace(trimmedValue, string.Empty);
            if (!string.IsNullOrWhiteSpace(normalizedValue))
            {
                candidates.Add(normalizedValue);
            }

            if (!string.IsNullOrWhiteSpace(trimmedValue))
            {
                candidates.Add(trimmedValue);
            }
        }

        private static HashSet<int> CaptureExistingProcessIds(IEnumerable<string> candidateProcessNames)
        {
            var processIds = new HashSet<int>();

            foreach (var processName in candidateProcessNames)
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    processIds.Add(process.Id);
                    process.Dispose();
                }
            }

            return processIds;
        }

        private static async Task<Process?> WaitForGameProcessAsync(
            IEnumerable<string> candidateProcessNames,
            HashSet<int> knownProcessIds,
            CancellationToken cancellationToken)
        {
            var timeoutAt = DateTimeOffset.UtcNow + ProcessDetectionTimeout;

            while (DateTimeOffset.UtcNow < timeoutAt)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var processName in candidateProcessNames)
                {
                    var gameProcess = TryGetNewProcess(processName, knownProcessIds);
                    if (gameProcess is not null)
                    {
                        return gameProcess;
                    }
                }

                await Task.Delay(ProcessPollingInterval, cancellationToken);
            }

            return null;
        }

        private static Process? TryGetNewProcess(string processName, HashSet<int> knownProcessIds)
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                if (knownProcessIds.Add(process.Id))
                {
                    return process;
                }

                process.Dispose();
            }

            return null;
        }
    }
}
