using Microsoft.EntityFrameworkCore;
using TeamGames.Core.Prompts;
using TeamGames.Infrastructure.Persistence;

namespace TeamGames.Infrastructure.Prompts;

public static class PromptSeeder
{
    public static async Task SeedAsync(TeamGamesDbContext database, IReadOnlyList<Prompt>? seeds = null, CancellationToken cancellationToken = default)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var existing = await database.Prompts.Where(p => p.RoomId == null).ToDictionaryAsync(p => p.Id, cancellationToken);
        var deleted = await database.PromptTombstones.Select(p => p.Id).ToArrayAsync(cancellationToken);
        foreach (var seed in seeds ?? PromptLibrary.All)
        {
            if (deleted.Contains(seed.Id)) continue;
            if (PromptRules.SeedConflicts(seed, existing.Values)) continue;
            var fingerprint = PromptRules.Fingerprint(seed);
            if (!existing.TryGetValue(seed.Id, out var current))
            {
                current = new Prompt { Id = seed.Id };
                database.Prompts.Add(current);
                existing.Add(seed.Id, current);
            }
            else if (!PromptRules.CanUpdateSeed(current)) continue; // Preserve administrator changes, including disabling.
            else if (current.SeedFingerprint == fingerprint) continue;
            current.GameId = seed.GameId;
            current.Category = seed.Category;
            current.Tags = seed.Tags;
            current.Text = seed.Text;
            current.OptionA = seed.OptionA;
            current.OptionB = seed.OptionB;
            current.Source = "built-in";
            current.Enabled = seed.Enabled;
            current.NormalizedKey = seed.NormalizedKey;
            current.SeedFingerprint = fingerprint;
            current.Revision++;
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
