using System.Collections.Generic;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CryoPod.Models;

namespace CryoPod.Services.Steam
{
    public interface ISteamAppDetailsService
    {
        Task<SteamAppDetailsResponse?> GetAppDetailsAsync(int steamAppId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<SteamAppDetailsResponse>> GetAppDetailsAsync(
            IEnumerable<int> steamAppIds,
            IProgress<SteamAppDetailsProgress>? progress = null,
            CancellationToken cancellationToken = default);
    }
}
