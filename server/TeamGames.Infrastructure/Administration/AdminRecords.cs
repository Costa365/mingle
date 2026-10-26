namespace TeamGames.Infrastructure.Administration;

public sealed class AdminSession
{
    public Guid Id { get; set; }
    public string Verifier { get; set; } = "";
    public string CredentialVersion { get; set; } = "";
    public long ExpiresTicks { get; set; }
}
// No content, names, credentials, or submitted values are retained here.
public sealed class AdminAudit
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid CommandId { get; set; }
    public long CreatedTicks { get; set; }
    public string Action { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public long AppliedVersion { get; set; }
}
public sealed class PromptTombstone
{
    public string Id { get; set; } = "";
}
