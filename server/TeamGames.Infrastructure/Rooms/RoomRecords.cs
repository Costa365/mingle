namespace TeamGames.Infrastructure.Rooms;

public sealed class GuestSession
{
    public Guid Id { get; set; }
    public string Verifier { get; set; } = "";
    public long ExpiresTicks { get; set; }
}

public sealed class CommandReceipt
{
    public Guid GuestId { get; set; }
    public Guid CommandId { get; set; }
    public Guid RoomId { get; set; }
    public string Fingerprint { get; set; } = "";
    public long AppliedVersion { get; set; }
}

// One process, one SQLite writer. No network operation is performed under this gate.
public sealed class RoomWriter
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
}
