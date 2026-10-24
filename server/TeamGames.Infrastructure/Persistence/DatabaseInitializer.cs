using Microsoft.EntityFrameworkCore;

namespace TeamGames.Infrastructure.Persistence;

public sealed class DatabaseInitializer(TeamGamesDbContext database, TimeProvider timeProvider)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await database.Database.MigrateAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);

        if (!await database.Installations.AnyAsync(cancellationToken))
        {
            database.Installations.Add(new Installation
            {
                Id = 1,
                InstanceId = Guid.NewGuid(),
                CreatedUtc = timeProvider.GetUtcNow().UtcDateTime
            });
            await database.SaveChangesAsync(cancellationToken);
        }
    }
}
