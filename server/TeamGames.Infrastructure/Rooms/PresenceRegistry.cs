namespace TeamGames.Infrastructure.Rooms;

public sealed class PresenceRegistry(TimeProvider time)
{
    public string Epoch { get; } = Guid.NewGuid().ToString();
    private readonly object sync = new();
    private readonly Dictionary<string, Subscription> connections = [];
    private readonly Dictionary<(Guid Room, Guid Participant), long> grace = [];
    private long revision;
    public sealed record Subscription(string ConnectionId, Guid RoomId, Guid GuestId, Guid ParticipantId, Action Abort);

    public void Subscribe(Subscription subscription)
    {
        lock (sync)
        {
            connections[subscription.ConnectionId] = subscription;
            grace.Remove((subscription.RoomId, subscription.ParticipantId));
            revision++;
        }
    }

    public Guid? Disconnect(string connectionId)
    {
        lock (sync)
        {
            if (!connections.Remove(connectionId, out var subscription)) return null;
            if (!connections.Values.Any(c => c.RoomId == subscription.RoomId && c.ParticipantId == subscription.ParticipantId))
                grace[(subscription.RoomId, subscription.ParticipantId)] = time.GetUtcNow().Ticks + TimeSpan.FromSeconds(5).Ticks;
            revision++;
            return subscription.RoomId;
        }
    }

    public (long Revision, HashSet<Guid> Online) Read(Guid roomId)
    {
        lock (sync)
        {
            var now = time.GetUtcNow().Ticks;
            return (revision, connections.Values.Where(c => c.RoomId == roomId).Select(c => c.ParticipantId)
                .Concat(grace.Where(g => g.Key.Room == roomId && g.Value > now).Select(g => g.Key.Participant)).ToHashSet());
        }
    }

    public Subscription[] Subscribers(Guid roomId)
    {
        lock (sync) return connections.Values.Where(c => c.RoomId == roomId).ToArray();
    }

    public Guid[] Sweep()
    {
        lock (sync)
        {
            var expired = grace.Where(g => g.Value <= time.GetUtcNow().Ticks).Select(g => g.Key).ToArray();
            foreach (var key in expired) grace.Remove(key);
            if (expired.Length > 0) revision++;
            return expired.Select(g => g.Room).Distinct().ToArray();
        }
    }
}
