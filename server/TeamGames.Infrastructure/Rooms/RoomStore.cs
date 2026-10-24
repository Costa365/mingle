using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamGames.Core.Rooms;
using TeamGames.Infrastructure.Persistence;

namespace TeamGames.Infrastructure.Rooms;

public sealed class RoomStore(TeamGamesDbContext database, RoomWriter writer, RoomSettings settings,
    TimeProvider time, PresenceRegistry presence)
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

    public async Task<RoomSnapshot> GetAsync(Guid id, Guid guestId)
    {
        if (!await database.Guests.AnyAsync(g => g.Id == guestId && g.ExpiresTicks > Now))
            throw new RoomException("membership_revoked", "Your guest session has expired", 401);
        var room = await Load(id);
        var viewer = Access(room, guestId);
        var online = presence.Read(id);
        return new(1, room.Id, room.Code, room.Version, online.Revision, presence.Epoch, room.Status, room.JoinLocked, settings.Capacity,
            viewer.Id, room.FacilitatorId,
            room.Members.Where(m => !m.Removed).OrderBy(m => m.Name, StringComparer.Ordinal)
                .Select(m => new ParticipantView(m.Id, m.Name, m.Id == room.FacilitatorId, online.Online.Contains(m.Id))).ToArray(),
            RoomRules.Actions(room, viewer));
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

    public async Task<CommandResult> ExecuteAsync(Guid roomId, Guid guestId, RoomCommand command)
    {
        ValidateId(command.CommandId);
        long version;
        bool replayed;
        await writer.Gate.WaitAsync();
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync();
            var room = await database.Rooms.Include(r => r.Members).SingleOrDefaultAsync(r => r.Id == roomId)
                ?? throw new RoomException("room_missing", "That room was not found", 404);
            var actor = Access(room, guestId);
            var fingerprint = Fingerprint(roomId.ToString(), command);
            var receipt = await Receipt(guestId, command.CommandId, fingerprint);
            replayed = receipt is not null;
            if (receipt is not null) version = receipt.AppliedVersion;
            else
            {
                RoomRules.Apply(room, actor, command, Now);
                AddReceipt(guestId, command.CommandId, fingerprint, room);
                await database.SaveChangesAsync();
                version = room.Version;
            }
            await transaction.CommitAsync();
        }
        finally { writer.Gate.Release(); }
        return new(await GetAsync(roomId, guestId), version, replayed);
    }

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
            var expired = await database.Rooms.Where(r => r.Status == "lobby" && r.LastActivityTicks <= idleBefore).ToArrayAsync();
            foreach (var room in expired)
            {
                room.Status = "expired";
                room.ClosedTicks = now;
                room.Version++;
            }
            var deleted = await database.Rooms.Where(r => r.ClosedTicks != null && r.ClosedTicks <= deleteBefore).ToArrayAsync();
            database.Rooms.RemoveRange(deleted);
            await database.SaveChangesAsync();
            await database.Guests.Where(g => g.ExpiresTicks <= now).ExecuteDeleteAsync();
            return expired.Select(r => r.Id).Concat(deleted.Select(r => r.Id)).Concat(expiredGuestRooms).Distinct().ToArray();
        }
        finally { writer.Gate.Release(); }
    }
}
