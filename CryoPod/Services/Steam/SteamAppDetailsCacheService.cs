using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CryoPod.Models;

namespace CryoPod.Services.Steam
{
    public sealed class SteamAppDetailsCacheService : ISteamAppDetailsCacheService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };

        private readonly SemaphoreSlim _cacheLock = new(1, 1);
        private readonly string _cacheFilePath;

        public SteamAppDetailsCacheService(string? cacheFilePath = null)
        {
            _cacheFilePath = cacheFilePath
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CryoPod",
                    "Cache",
                    "steam-app-details.json");
        }

        public async Task<IReadOnlyDictionary<int, CachedSteamAppDetails>> GetCachedAppDetailsAsync(
            IEnumerable<int> steamAppIds,
            CancellationToken cancellationToken = default)
        {
            var requestedIds = steamAppIds.Where(id => id > 0).Distinct().ToHashSet();
            if (requestedIds.Count == 0)
            {
                return new Dictionary<int, CachedSteamAppDetails>();
            }

            await _cacheLock.WaitAsync(cancellationToken);
            try
            {
                var cache = await ReadCacheAsync(cancellationToken);
                return cache
                    .Where(entry => requestedIds.Contains(entry.Key))
                    .ToDictionary(entry => entry.Key, entry => entry.Value);
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        public async Task SaveAppDetailsAsync(
            IEnumerable<SteamAppDetailsResponse> appDetails,
            CancellationToken cancellationToken = default)
        {
            var detailsToSave = appDetails
                .Where(details => details.Success && details.Data?.SteamAppId > 0)
                .ToList();

            if (detailsToSave.Count == 0)
            {
                return;
            }

            await _cacheLock.WaitAsync(cancellationToken);
            try
            {
                var cache = await ReadCacheAsync(cancellationToken);
                var updatedAt = DateTimeOffset.UtcNow;

                foreach (var details in detailsToSave)
                {
                    cache[details.Data!.SteamAppId] = new CachedSteamAppDetails
                    {
                        LastUpdatedUtc = updatedAt,
                        AppDetails = details,
                    };
                }

                await WriteCacheAsync(cache, cancellationToken);
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        private async Task<Dictionary<int, CachedSteamAppDetails>> ReadCacheAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(_cacheFilePath))
            {
                return new Dictionary<int, CachedSteamAppDetails>();
            }

            await using var cacheStream = File.OpenRead(_cacheFilePath);
            var cache = await JsonSerializer.DeserializeAsync<Dictionary<int, CachedSteamAppDetails>>(
                cacheStream,
                SerializerOptions,
                cancellationToken);

            return cache ?? new Dictionary<int, CachedSteamAppDetails>();
        }

        private async Task WriteCacheAsync(Dictionary<int, CachedSteamAppDetails> cache, CancellationToken cancellationToken)
        {
            var cacheDirectory = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrWhiteSpace(cacheDirectory))
            {
                Directory.CreateDirectory(cacheDirectory);
            }

            await using var cacheStream = File.Create(_cacheFilePath);
            await JsonSerializer.SerializeAsync(cacheStream, cache, SerializerOptions, cancellationToken);
        }
    }
}
