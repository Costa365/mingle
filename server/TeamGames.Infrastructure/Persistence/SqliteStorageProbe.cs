using Microsoft.EntityFrameworkCore;
using TeamGames.Core.Storage;

namespace TeamGames.Infrastructure.Persistence;

public sealed class SqliteStorageProbe(TeamGamesDbContext database) : IStorageProbe
{
    public Task<bool> IsReadyAsync(CancellationToken cancellationToken) =>
        database.Installations.AsNoTracking().AnyAsync(installation => installation.Id == 1, cancellationToken);
}
