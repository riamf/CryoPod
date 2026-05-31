using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CryoPod.Models;

namespace CryoPod.Services.GameExplorer
{
    public sealed class PlaceholderGameExplorerService : IGameExplorerService
    {
        public string SourceName => "Placeholder";

        public Task<IReadOnlyList<InstalledGame>> FindInstalledGamesAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<InstalledGame> games = [];
            return Task.FromResult(games);
        }
    }
}
