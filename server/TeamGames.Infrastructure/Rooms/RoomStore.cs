using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamGames.Core.Rooms;
using TeamGames.Core.Prompts;
using TeamGames.Core.Games;
using TeamGames.Infrastructure.Persistence;
using TeamGames.Infrastructure.Prompts;

namespace TeamGames.Infrastructure.Rooms;

public sealed partial class RoomStore(TeamGamesDbContext database, RoomWriter writer, RoomSettings settings,
    TimeProvider time, PresenceRegistry presence, GameModules games, TeamGames.Core.Administration.AdminSettings adminSettings, PromptCache promptCache)
{
    private long Now => time.GetUtcNow().Ticks;
    private static string Fingerprint(string operation, object payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operation + JsonSerializer.Serialize(payload))));
    private static void ValidateId(Guid commandId)
    {
        if (commandId == Guid.Empty) throw new RoomException("invalid_command", "A command ID is required");
    }
    private async Task<Room> Load(Guid id) =>
        await database.Rooms.AsNoTracking().Include(r => r.Members).SingleOrDefaultAsync(r => r.Id == id)
        ?? throw new RoomException("room_missing", "That room was not found", 404);

    private Membership Access(Room room, Guid guestId)
    {
        var member = RoomRules.Authorize(room, guestId);
        if (RoomRules.IsExpired(room, Now, settings)) throw new RoomException("room_expired", "This room has expired", 410);
        return member;
    }

    public sealed record Delivery(RoomSnapshot? Snapshot, RoomException? Error);
    public async Task<RoomSnapshot> GetAsync(Guid id, Guid guestId)
    {
        var result = (await GetManyAsync(id, [guestId]))[guestId];
        return result.Snapshot ?? throw result.Error!;
    }
    // One durable read, with separate authorization and projection for every recipient.
    public async Task<IReadOnlyDictionary<Guid, Delivery>> GetManyAsync(Guid id, IReadOnlyCollection<Guid> guestIds)
    {
        var now = Now;
        var valid = await database.Guests.AsNoTracking().Where(g => guestIds.Contains(g.Id) && g.ExpiresTicks > now).Select(g => g.Id).ToArrayAsync();
        Room? room = null;
        RoomException? loadError = null;
        try { room = await Load(id); await PopulatePrompts(room); }
        catch (RoomException error) { loadError = error; }
        var online = presence.Read(id);
        var participants = room?.Members.Where(m => !m.Removed).OrderBy(m => m.Name, StringComparer.Ordinal)
            .Select(m => new ParticipantView(m.Id, m.Name, m.Id == room.FacilitatorId, online.Online.Contains(m.Id))).ToArray() ?? [];
        var result = new Dictionary<Guid, Delivery>();
        foreach (var guestId in guestIds.Distinct())
        {
            try
            {
                if (!valid.Contains(guestId)) throw new RoomException("membership_revoked", "Your guest session has expired", 401);
                if (loadError is not null) throw loadError;
                var viewer = Access(room!, guestId);
                result[guestId] = new(new(1, room!.Id, room.Code, room.Version, online.Revision, presence.Epoch, room.Status,
                    room.JoinLocked, settings.Capacity, viewer.Id, room.FacilitatorId, participants, RoomRules.Actions(room, viewer), games.Project(room, viewer)), null);
            }
            catch (RoomException error) { result[guestId] = new(null, error); }
        }
        return result;
    }

    private async Task<CommandReceipt?> Receipt(Guid guestId, Guid commandId, string fingerprint)
    {
        var receipt = await database.Receipts.SingleOrDefaultAsync(r => r.GuestId == guestId && r.CommandId == commandId);
        if (receipt is not null && receipt.Fingerprint != fingerprint)
            throw new RoomException("command_reused", "That command ID was already used for a different request", 409);
        return receipt;
    }

    private void AddReceipt(Guid guestId, Guid commandId, string fingerprint, Room room) =>
        database.Receipts.Add(new CommandReceipt { GuestId = guestId, CommandId = commandId, RoomId = room.Id, Fingerprint = fingerprint, AppliedVersion = room.Version });

    public async Task<CommandResult> EnterAsync(Guid guestId, EntryRequest request, bool create)
    {
        ValidateId(request.CommandId);
        var name = RoomRules.DisplayName(request.DisplayName);
        var code = create ? null : RoomRules.InviteCode(request.Code);
        var fingerprint = Fingerprint(create ? "create" : "join", request);
        Guid roomId;
        long version;
        var replayed = false;
        await writer.Gate.WaitAsync();
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync();
            Room room;
            if (create)
            {
                var previous = await database.Receipts.SingleOrDefaultAsync(r => r.GuestId == guestId && r.CommandId == request.CommandId);
                if (previous is not null)
                {
                    room = await Load(previous.RoomId);
                    Access(room, guestId);
                }
                else
                {
                    string invite;
                    do { invite = RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZ23456789", 6); }
                    while (await database.Rooms.AnyAsync(r => r.Code == invite));
                    room = new Room { Id = Guid.NewGuid(), Code = invite, LastActivityTicks = Now };
                    room.FacilitatorId = RoomRules.Join(room, guestId, name, settings.Capacity).Id;
                    database.Rooms.Add(room);
                }
            }
            else
            {
                room = await database.Rooms.Include(r => r.Members).SingleOrDefaultAsync(r => r.Code == code)
                    ?? throw new RoomException("room_missing", "That room was not found", 404);
                if (room.Members.Any(m => m.GuestId == guestId && m.Removed))
                    throw new RoomException("membership_revoked", "You were removed from this room", 403);
                if (RoomRules.IsExpired(room, Now, settings)) throw new RoomException("room_expired", "This room has expired", 410);
            }
            var receipt = await Receipt(guestId, request.CommandId, fingerprint);
            if (receipt is not null)
            {
                // Do not return receipts to a removed or expired participant.
                Access(room, guestId);
                version = receipt.AppliedVersion;
                replayed = true;
            }
            else
            {
                if (!create)
                {
                    var count = room.Members.Count;
                    var member = RoomRules.Join(room, guestId, name, settings.Capacity);
                    if (room.Members.Count != count)
                    {
                        database.Memberships.Add(member);
                        room.Version++;
                    }
                    room.LastActivityTicks = Now;
                }
                AddReceipt(guestId, request.CommandId, fingerprint, room);
                await database.SaveChangesAsync();
                version = room.Version;
            }
            roomId = room.Id;
            await transaction.CommitAsync();
        }
        finally { writer.Gate.Release(); }
        return new(await GetAsync(roomId, guestId), version, replayed);
    }

    public Task<CommandResult> ExecuteAsync(Guid roomId, Guid guestId, RoomCommand command) =>
        Execute(roomId, guestId, command.CommandId, command, (room, actor) => RoomRules.Apply(room, actor, command, Now));

    public async Task<CommandResult> PlayAsync(Guid roomId, Guid guestId, GameCommand command)
    {
        var reply = (await PlayBatchAsync(roomId, [new(guestId, command)]))[0];
        return reply.Result ?? throw reply.Error!;
    }

    private async Task<CommandResult> Execute(Guid roomId, Guid guestId, Guid commandId, object command, Action<Room, Membership>? apply, Func<Room, Membership, Task>? applyAsync = null, bool invalidatePrompts = false)
    {
        ValidateId(commandId);
        long version;
        RoomSnapshot snapshot;
        bool replayed;
        await writer.Gate.WaitAsync();
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync();
            var room = await database.Rooms.Include(r => r.Members).SingleOrDefaultAsync(r => r.Id == roomId)
                ?? throw new RoomException("room_missing", "That room was not found", 404);
            var actor = Access(room, guestId);
            var fingerprint = Fingerprint(roomId.ToString(), command);
            var receipt = await Receipt(guestId, commandId, fingerprint);
            replayed = receipt is not null;
            if (receipt is not null) { version = receipt.AppliedVersion; await PopulatePrompts(room); }
            else
            {
                await PopulatePrompts(room);
                if (applyAsync is not null) await applyAsync(room, actor);
                else apply!(room, actor);
                AddReceipt(guestId, commandId, fingerprint, room);
                await database.SaveChangesAsync();
                version = room.Version;
            }
            await transaction.CommitAsync();
            if (invalidatePrompts) { promptCache.Invalidate(); await PopulatePrompts(room); }
            var online = presence.Read(room.Id);
            snapshot = new(1, room.Id, room.Code, room.Version, online.Revision, presence.Epoch, room.Status, room.JoinLocked, settings.Capacity,
                actor.Id, room.FacilitatorId, room.Members.Where(m => !m.Removed).OrderBy(m => m.Name, StringComparer.Ordinal)
                    .Select(m => new ParticipantView(m.Id, m.Name, m.Id == room.FacilitatorId, online.Online.Contains(m.Id))).ToArray(),
                RoomRules.Actions(room, actor), games.Project(room, actor));
        }
        finally { writer.Gate.Release(); }
        return new(snapshot, version, replayed);
    }

    private async Task PopulatePrompts(Room room) => room.PromptPool = await promptCache.ReadAsync(room.Id, () => database.Prompts.AsNoTracking()
        .Where(p => p.Enabled && (p.RoomId == null || p.RoomId == room.Id)).OrderBy(p => p.Id).ToArrayAsync());

    public async Task<PromptPage> PromptsAsync(Guid roomId, Guid guestId, string? gameId, string? category, string? tag, string? search)
    {
        if ((search?.Length ?? 0) > 120 || (category?.Length ?? 0) > 48 || (tag?.Length ?? 0) > 32)
            throw new RoomException("invalid_filter", "Shorten the prompt filters");
        var room = await Load(roomId);
        var actor = Access(room, guestId);
        GameModules.Facilitator(room, actor);
        if (room.Status is not ("lobby" or "playing")) throw new RoomException("room_closed", "This room is closed", 410);
        if (gameId is not null && !PromptRules.GameIds.Contains(gameId)) throw new RoomException("invalid_filter", "Choose a supported prompt game");
        await PopulatePrompts(room);
        var used = games.Project(room, actor)?.UsedPromptIds ?? [];
        var all = await database.Prompts.AsNoTracking().Where(p => p.RoomId == null || p.RoomId == roomId).OrderBy(p => p.Id).ToArrayAsync();
        return new(room.Version, all.Where(p => PromptRules.Matches(p, gameId ?? p.GameId, category, tag, search) ||
            (!p.Enabled && p.RoomId == roomId && PromptRules.Matches(CloneEnabled(p), gameId ?? p.GameId, category, tag, search)))
            .Select(p => PromptRules.View(p, used.Contains(p.Id))).ToArray());
    }
    private static Prompt CloneEnabled(Prompt prompt) => new()
    {
        GameId = prompt.GameId,
        Enabled = true,
        Category = prompt.Category,
        Tags = prompt.Tags,
        Text = prompt.Text,
        OptionA = prompt.OptionA,
        OptionB = prompt.OptionB
    };

    public Task<CommandResult> EditPromptAsync(Guid roomId, Guid guestId, PromptCommand command) =>
        Execute(roomId, guestId, command.CommandId, command, null, async (room, actor) =>
        {
            GameModules.Facilitator(room, actor);
            if (room.Status is not ("lobby" or "playing")) throw new RoomException("room_closed", "This room is closed", 410);
            if (room.Version != command.ExpectedVersion) throw new RoomException("version_changed", "The room changed; review it and try again", 409);
            Prompt? current = null;
            if (command.Action is "edit" or "delete")
            {
                current = await database.Prompts.SingleOrDefaultAsync(p => p.Id == command.PromptId && p.RoomId == roomId)
                    ?? throw new RoomException("prompt_missing", "That room prompt was not found", 404);
                if (current.Revision != command.PromptRevision) throw new RoomException("prompt_changed", "The prompt changed; review it and try again", 409);
            }
            if (command.Action == "delete") database.Prompts.Remove(current!);
            else if (command.Action is "create" or "edit")
            {
                var candidate = PromptRules.Create(command.Input ?? throw new RoomException("invalid_prompt", "Enter prompt content"),
                    current?.Id ?? Guid.NewGuid().ToString(), roomId);
                if (await database.Prompts.AnyAsync(p => p.Id != candidate.Id && (p.RoomId == null || p.RoomId == roomId) && p.NormalizedKey == candidate.NormalizedKey))
                    throw new RoomException("duplicate_prompt", "That prompt already exists in this room's library", 409);
                if (current is null) database.Prompts.Add(candidate);
                else
                {
                    database.Entry(current).CurrentValues.SetValues(candidate);
                    current.Revision = command.PromptRevision + 1;
                }
            }
            else throw new RoomException("invalid_command", "That prompt action is not supported");
            room.Version++;
            room.LastActivityTicks = Now;
        }, invalidatePrompts: true);

    public async Task<Guid[]> SweepAsync()
    {
        await writer.Gate.WaitAsync();
        try
        {
            var now = Now;
            var expiredGuestRooms = await database.Memberships
                .Where(m => database.Guests.Any(g => g.Id == m.GuestId && g.ExpiresTicks <= now))
                .Select(m => m.RoomId).Distinct().ToArrayAsync();
            var idleBefore = now - TimeSpan.FromHours(settings.IdleHours).Ticks;
            var deleteBefore = now - TimeSpan.FromHours(settings.RetentionHours).Ticks;
            var expired = await database.Rooms.Where(r => (r.Status == "lobby" || r.Status == "playing") && r.LastActivityTicks <= idleBefore).ToArrayAsync();
            foreach (var room in expired)
            {
                room.Status = "expired";
                room.ClosedTicks = now;
                room.Version++;
            }
            var deleted = await database.Rooms.Where(r => r.ClosedTicks != null && r.ClosedTicks <= deleteBefore).ToArrayAsync();
            database.Rooms.RemoveRange(deleted);
            if (deleted.Length > 0) promptCache.Invalidate();
            await database.SaveChangesAsync();
            await database.Guests.Where(g => g.ExpiresTicks <= now).ExecuteDeleteAsync();
            await database.AdminSessions.Where(s => s.ExpiresTicks <= now || s.CredentialVersion != adminSettings.CredentialVersion).ExecuteDeleteAsync();
            var auditBefore = now - TimeSpan.FromDays(adminSettings.AuditRetentionDays).Ticks;
            await database.AdminAudits.Where(a => a.CreatedTicks <= auditBefore).ExecuteDeleteAsync();
            return expired.Select(r => r.Id).Concat(deleted.Select(r => r.Id)).Concat(expiredGuestRooms).Distinct().ToArray();
        }
        finally { writer.Gate.Release(); }
    }
}
