using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CryoPod.Models;

namespace CryoPod.Services.Steam
{
    public interface ISteamAppDetailsCacheService
    {
        Task<IReadOnlyDictionary<int, CachedSteamAppDetails>> GetCachedAppDetailsAsync(
            IEnumerable<int> steamAppIds,
            CancellationToken cancellationToken = default);

        Task SaveAppDetailsAsync(
            IEnumerable<SteamAppDetailsResponse> appDetails,
            CancellationToken cancellationToken = default);
    }
}
