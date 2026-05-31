using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CryoPod.Models;
using Microsoft.Win32;

namespace CryoPod.Services.GameExplorer
{
    public sealed class SteamGameExplorerService : IGameExplorerService
    {
        private const string SteamRegistryPath = @"SOFTWARE\WOW6432Node\Valve\Steam";
        private static readonly Regex QuotedValueRegex = new("\"([^\"]*)\"", RegexOptions.Compiled);

        public string SourceName => "Steam";

        public Task<IReadOnlyList<InstalledGame>> FindInstalledGamesAsync(CancellationToken cancellationToken = default)
        {
            var games = new List<InstalledGame>();
            var steamInstallPath = GetSteamInstallPath();

            if (string.IsNullOrWhiteSpace(steamInstallPath))
            {
                return Task.FromResult<IReadOnlyList<InstalledGame>>(games);
            }

            var libraryPaths = GetLibraryPaths(steamInstallPath);

            foreach (var libraryPath in libraryPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var steamAppsPath = Path.Combine(libraryPath, "steamapps");
                if (!Directory.Exists(steamAppsPath))
                {
                    continue;
                }

                var manifestFiles = Directory.GetFiles(steamAppsPath, "appmanifest_*.acf", SearchOption.TopDirectoryOnly);
                foreach (var manifestFile in manifestFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var game = ParseInstalledGame(manifestFile, libraryPath);
                    if (game is not null)
                    {
                        games.Add(game);
                    }
                }
            }

            return Task.FromResult<IReadOnlyList<InstalledGame>>(games);
        }

        private static string? GetSteamInstallPath()
        {
            using var steamKey = Registry.LocalMachine.OpenSubKey(SteamRegistryPath);
            return steamKey?.GetValue("InstallPath") as string;
        }

        private static HashSet<string> GetLibraryPaths(string steamInstallPath)
        {
            var libraryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                steamInstallPath,
            };

            var libraryFoldersPath = Path.Combine(steamInstallPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFoldersPath))
            {
                return libraryPaths;
            }

            var lines = File.ReadAllLines(libraryFoldersPath);
            string? currentLibraryPath = null;

            foreach (var line in lines)
            {
                var values = ExtractQuotedValues(line);
                if (values.Count < 2)
                {
                    continue;
                }

                if (values[0].Equals("path", StringComparison.OrdinalIgnoreCase))
                {
                    currentLibraryPath = NormalizeSteamPath(values[1]);
                    if (!string.IsNullOrWhiteSpace(currentLibraryPath) && Directory.Exists(currentLibraryPath))
                    {
                        libraryPaths.Add(currentLibraryPath);
                    }

                    continue;
                }

                if (int.TryParse(values[0], out _) && Directory.Exists(NormalizeSteamPath(values[1])))
                {
                    libraryPaths.Add(NormalizeSteamPath(values[1]));
                }
            }

            return libraryPaths;
        }

        private static InstalledGame? ParseInstalledGame(string manifestFilePath, string libraryPath)
        {
            if (!File.Exists(manifestFilePath))
            {
                return null;
            }

            var values = ParseKeyValueFile(manifestFilePath);
            if (!values.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            values.TryGetValue("installdir", out var installDir);

            string? installPath = null;
            if (!string.IsNullOrWhiteSpace(installDir))
            {
                installPath = Path.Combine(libraryPath, "steamapps", "common", installDir);
            }

            return new InstalledGame(name, "Steam", installPath);
        }

        private static Dictionary<string, string> ParseKeyValueFile(string filePath)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in File.ReadLines(filePath))
            {
                var quotedValues = ExtractQuotedValues(line);
                if (quotedValues.Count >= 2)
                {
                    values[quotedValues[0]] = NormalizeSteamPath(quotedValues[1]);
                }
            }

            return values;
        }

        private static List<string> ExtractQuotedValues(string input)
        {
            var values = new List<string>();
            var matches = QuotedValueRegex.Matches(input);
            foreach (Match match in matches)
            {
                values.Add(match.Groups[1].Value);
            }

            return values;
        }

        private static string NormalizeSteamPath(string path)
        {
            return path.Replace("\\\\", "\\");
        }
    }
}
