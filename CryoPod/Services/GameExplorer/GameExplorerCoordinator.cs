using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CryoPod.Models;

namespace CryoPod.Services.GameExplorer
{
    public sealed class GameExplorerCoordinator
    {
        private readonly IReadOnlyList<IGameExplorerService> _services;

        public GameExplorerCoordinator(IEnumerable<IGameExplorerService> services)
        {
            _services = services.ToList();
        }

        public async Task<IReadOnlyList<InstalledGame>> FindInstalledGamesAsync(CancellationToken cancellationToken = default)
        {
            var games = new List<InstalledGame>();

            foreach (var service in _services)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var discoveredGames = await service.FindInstalledGamesAsync(cancellationToken);
                games.AddRange(discoveredGames);
            }

            return games;
        }
    }
}
