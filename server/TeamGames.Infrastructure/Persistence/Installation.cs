namespace TeamGames.Infrastructure.Persistence;

// A durable installation marker lets startup and readiness distinguish an
// initialized schema from an empty database. It contains no player information.
public sealed class Installation
{
    public int Id { get; set; }
    public Guid InstanceId { get; set; }
    public DateTime CreatedUtc { get; set; }
}
