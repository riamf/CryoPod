using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CryoPod.Models;

namespace CryoPod.Services.GameExplorer
{
    public interface IGameExplorerService
    {
        string SourceName { get; }

        Task<IReadOnlyList<InstalledGame>> FindInstalledGamesAsync(CancellationToken cancellationToken = default);
    }
}
