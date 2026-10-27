using System.Collections.Concurrent;
using TeamGames.Core.Prompts;

namespace TeamGames.Infrastructure.Prompts;

// Only prompt content is cached. Membership, credentials and game answers never are.
public sealed class PromptCache(int capacity = 128)
{
    private sealed record Entry(long Epoch, IReadOnlyList<Prompt> Prompts);
    private readonly ConcurrentDictionary<Guid, Entry> rooms = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private long epoch;
    private readonly int limit = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    public void Invalidate() { Interlocked.Increment(ref epoch); rooms.Clear(); }
    public void Remove(Guid roomId) => Invalidate(); // Also rejects an in-flight old load.
    public async Task<IReadOnlyList<Prompt>> ReadAsync(Guid roomId, Func<Task<Prompt[]>> load)
    {
        while (true)
        {
            var version = Volatile.Read(ref epoch);
            if (rooms.TryGetValue(roomId, out var cached) && cached.Epoch == version) return cached.Prompts;
            await gate.WaitAsync();
            try
            {
                version = Volatile.Read(ref epoch);
                if (rooms.TryGetValue(roomId, out cached) && cached.Epoch == version) return cached.Prompts;
                var prompts = await load();
                if (version != Volatile.Read(ref epoch)) continue;
                if (rooms.Count >= limit) rooms.Clear();
                rooms[roomId] = new(version, prompts);
                return prompts;
            }
            finally { gate.Release(); }
        }
    }
}
