using System.Text;
using TeamGames.Core.Prompts;
using TeamGames.Core.Games;

namespace TeamGames.Core.Rooms;

public sealed class Room
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public long Version { get; set; } = 1;
    public bool JoinLocked { get; set; }
    public string Status { get; set; } = "lobby";
    public string? GameState { get; set; }
    public Guid FacilitatorId { get; set; }
    public long LastActivityTicks { get; set; }
    public long? ClosedTicks { get; set; }
    public IReadOnlyList<Prompt> PromptPool { get; set; } = [];
    public List<Membership> Members { get; set; } = [];
}

public sealed class Membership
{
    public Guid Id { get; set; }
    public Guid RoomId { get; set; }
    public Guid GuestId { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public bool Removed { get; set; }
}

public sealed class RoomException(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public sealed class RoomSettings
{
    public int Capacity { get; set; } = 20;
    public int IdleHours { get; set; } = 24;
    public int RetentionHours { get; set; } = 24;
    public int GuestDays { get; set; } = 7;
}

public sealed record EntryRequest(Guid CommandId, string DisplayName, string? Code = null);
public sealed record RoomCommand(Guid CommandId, long ExpectedVersion, string Action, Guid? TargetId = null, bool? Locked = null);
public sealed record ParticipantView(Guid Id, string Name, bool IsFacilitator, bool IsOnline);
public sealed record RoomSnapshot(int ProtocolVersion, Guid Id, string Code, long Version, long PresenceRevision, string PresenceEpoch,
    string Status, bool JoinLocked, int Capacity, Guid YouId, Guid FacilitatorId,
    IReadOnlyList<ParticipantView> Participants, IReadOnlyList<string> AllowedActions, GameView? Game = null);
public sealed record CommandResult(RoomSnapshot Snapshot, long AppliedVersion, bool Replayed);
public sealed record SessionView(string RequestToken);
public sealed record RoomErrorView(string Code, string Message);

public static class RoomRules
{
    public static string DisplayName(string? input)
    {
        var name = (input ?? "").Trim().Normalize(NormalizationForm.FormKC);
        if (name.Length is < 1 or > 32 || name.Any(char.IsControl))
            throw new RoomException("invalid_name", "Use a name of 1–32 characters without control characters");
        return name;
    }

    public static string NormalizeName(string name) => name.ToUpperInvariant();

    public static string InviteCode(string? input)
    {
        var code = (input ?? "").Trim().ToUpperInvariant();
        if (code.Length != 6 || code.Any(c => !"ABCDEFGHJKLMNPQRSTUVWXYZ23456789".Contains(c)))
            throw new RoomException("invalid_code", "Enter a six-character room code");
        return code;
    }

    public static Membership Authorize(Room room, Guid guestId) =>
        room.Members.FirstOrDefault(m => m.GuestId == guestId && !m.Removed)
        ?? throw new RoomException("membership_revoked", "You do not have access to this room", 403);

    public static bool IsExpired(Room room, long now, RoomSettings settings) =>
        room.Status == "expired" || ((room.Status is "lobby" or "playing") && now - room.LastActivityTicks >= TimeSpan.FromHours(settings.IdleHours).Ticks);

    public static Membership Join(Room room, Guid guestId, string name, int capacity)
    {
        var existing = room.Members.FirstOrDefault(m => m.GuestId == guestId);
        if (existing?.Removed == true) throw new RoomException("membership_revoked", "You were removed from this room", 403);
        if (existing is not null) return existing;
        if (room.Status is not ("lobby" or "playing")) throw new RoomException("room_closed", "This room is closed", 410);
        if (room.JoinLocked) throw new RoomException("room_locked", "The facilitator has locked joining", 409);
        if (room.Members.Count(m => !m.Removed) >= capacity) throw new RoomException("room_full", "This room is full", 409);
        var normalized = NormalizeName(name);
        if (room.Members.Any(m => !m.Removed && m.NormalizedName == normalized))
            throw new RoomException("name_taken", "Someone already uses that name in this room", 409);
        var member = new Membership { Id = Guid.NewGuid(), RoomId = room.Id, GuestId = guestId, Name = name, NormalizedName = normalized };
        room.Members.Add(member);
        return member;
    }

    public static void Apply(Room room, Membership actor, RoomCommand command, long now)
    {
        if (actor.Id != room.FacilitatorId) throw new RoomException("facilitator_required", "Only the facilitator can do that", 403);
        ApplyManagement(room, command, now, actor.Id);
    }

    // Both facilitator and administrator commands use these state transitions.
    public static void ApplyManagement(Room room, RoomCommand command, long now, Guid? actorId = null)
    {
        if (room.Status is not ("lobby" or "playing")) throw new RoomException("room_closed", "This room is closed", 410);
        if (command.ExpectedVersion != room.Version) throw new RoomException("version_changed", "The room changed; review it and try again", 409);
        switch (command.Action)
        {
            case "lock":
                if (command.Locked is null) throw new RoomException("invalid_command", "Choose whether joining is locked");
                room.JoinLocked = command.Locked.Value;
                break;
            case "remove":
            case "transfer":
                var target = room.Members.FirstOrDefault(m => m.Id == command.TargetId && !m.Removed);
                if (target is null || target.Id == actorId || command.Action == "remove" && target.Id == room.FacilitatorId)
                    throw new RoomException("invalid_target", "Choose another current participant");
                if (command.Action == "remove") target.Removed = true;
                else room.FacilitatorId = target.Id;
                break;
            case "close":
                room.Status = "closed";
                room.ClosedTicks = now;
                break;
            default: throw new RoomException("invalid_command", "That room action is not supported");
        }
        room.Version++;
        room.LastActivityTicks = now;
    }

    public static IReadOnlyList<string> Actions(Room room, Membership member) =>
        (room.Status is "lobby" or "playing") && member.Id == room.FacilitatorId ? ["lock", "remove", "transfer", "close"] : [];
}
