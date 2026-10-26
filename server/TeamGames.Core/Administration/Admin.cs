using System.Security.Cryptography;
using System.Text;
using TeamGames.Core.Rooms;
using TeamGames.Core.Prompts;

namespace TeamGames.Core.Administration;

public sealed class AdminSettings
{
    public string? Credential { get; set; }
    public int SessionMinutes { get; set; } = 60;
    public int AuditRetentionDays { get; set; } = 7;
    public int LoginRateLimit { get; set; } = 5;
    public bool Enabled => !string.IsNullOrWhiteSpace(Credential);
    public string CredentialVersion => Hash(Credential ?? "");
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public bool Accepts(string? value) => Enabled && value is { Length: <= 1024 } && CryptographicOperations.FixedTimeEquals(
        Convert.FromHexString(Hash(value)), Convert.FromHexString(CredentialVersion));
    public void Validate()
    {
        if (Enabled && Credential!.Length is < 16 or > 1024 || SessionMinutes is < 1 or > 1440 || AuditRetentionDays is < 1 or > 30 || LoginRateLimit is < 1 or > 100)
            throw new InvalidOperationException("Invalid administrator configuration");
    }
}
public sealed record AdminSessionView(bool Enabled, bool Authenticated, string RequestToken, long? ExpiresTicks);
public sealed record AdminLogin(string Credential);
public sealed record AdminGameView(Guid SessionId, string GameId, string Stage, int RoundNumber);
public sealed record AdminRoomView(Guid Id, string Code, long Version, string Status, bool JoinLocked, int ParticipantCount,
    long LastActivityTicks, Guid FacilitatorId, IReadOnlyList<ParticipantView> Participants, IReadOnlyList<AdminGameView> Games);
public sealed record AdminRoomPage(int Page, int PageSize, int Total, IReadOnlyList<AdminRoomView> Rooms);
public sealed record AdminRoomCommand(Guid CommandId, long ExpectedVersion, string Action, Guid? TargetId = null, bool? Locked = null, string? Confirmation = null);
public sealed record AdminPromptCommand(Guid CommandId, string Action, PromptInput? Input = null, string? PromptId = null, long PromptRevision = 0);
public sealed record AdminPromptPage(int Page, int PageSize, int Total, IReadOnlyList<PromptView> Prompts);
public sealed record AdminAuditView(Guid Id, long CreatedTicks, string Action, string TargetId);
public sealed record AdminAuditPage(int Page, int PageSize, int Total, IReadOnlyList<AdminAuditView> Events);
public sealed record AdminCommandResult(long AppliedVersion, bool Replayed);

public static class AdminRules
{
    public static void Pagination(int page, int pageSize)
    {
        if (page is < 1 or > 1000000 || pageSize is < 1 or > 100) throw new RoomException("invalid_page", "Choose a page size between 1 and 100");
    }
    public static void ConfirmDeletion(Room room, AdminRoomCommand command)
    {
        if (command.Confirmation != room.Code) throw new RoomException("confirmation_required", "Enter the room code to confirm deletion");
    }
}
