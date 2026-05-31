using System;

namespace CryoPod.Models
{
    public sealed class CachedSteamAppDetails
    {
        public DateTimeOffset LastUpdatedUtc { get; set; }

        public SteamAppDetailsResponse? AppDetails { get; set; }
    }
}
