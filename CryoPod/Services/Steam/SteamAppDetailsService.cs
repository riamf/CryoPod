using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CryoPod.Models;

namespace CryoPod.Services.Steam
{
    public sealed class SteamAppDetailsService : ISteamAppDetailsService
    {
        private static readonly TimeSpan RequestDelay = TimeSpan.FromSeconds(2);
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _requestLock = new(1, 1);
        private DateTimeOffset? _lastRequestCompletedAt;

        public SteamAppDetailsService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<SteamAppDetailsResponse?> GetAppDetailsAsync(int steamAppId, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(steamAppId);

            await _requestLock.WaitAsync(cancellationToken);
            try
            {
                await DelayIfNeededAsync(cancellationToken);

                using var response = await _httpClient.GetAsync($"https://store.steampowered.com/api/appdetails?appids={steamAppId}", cancellationToken);
                response.EnsureSuccessStatusCode();

                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);

                if (!document.RootElement.TryGetProperty(steamAppId.ToString(CultureInfo.InvariantCulture), out var appDetailsElement))
                {
                    return null;
                }

                return appDetailsElement.Deserialize<SteamAppDetailsResponse>(SerializerOptions);
            }
            finally
            {
                _lastRequestCompletedAt = DateTimeOffset.UtcNow;
                _requestLock.Release();
            }
        }

        public async Task<IReadOnlyList<SteamAppDetailsResponse>> GetAppDetailsAsync(IEnumerable<int> steamAppIds, CancellationToken cancellationToken = default)
        {
            return await GetAppDetailsAsync(steamAppIds, progress: null, cancellationToken);
        }

        public async Task<IReadOnlyList<SteamAppDetailsResponse>> GetAppDetailsAsync(
            IEnumerable<int> steamAppIds,
            IProgress<SteamAppDetailsProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var results = new List<SteamAppDetailsResponse>();
            var appIds = steamAppIds.Where(id => id > 0).Distinct().ToList();

            for (var index = 0; index < appIds.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var steamAppId = appIds[index];
                progress?.Report(new SteamAppDetailsProgress(steamAppId, index + 1, appIds.Count));

                var appDetails = await GetAppDetailsAsync(steamAppId, cancellationToken);
                if (appDetails?.Success == true)
                {
                    results.Add(appDetails);
                }
            }

            return results;
        }

        private async Task DelayIfNeededAsync(CancellationToken cancellationToken)
        {
            if (_lastRequestCompletedAt is null)
            {
                return;
            }

            var elapsed = DateTimeOffset.UtcNow - _lastRequestCompletedAt.Value;
            var remainingDelay = RequestDelay - elapsed;
            if (remainingDelay > TimeSpan.Zero)
            {
                await Task.Delay(remainingDelay, cancellationToken);
            }
        }
    }
}
