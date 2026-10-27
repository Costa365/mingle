using Microsoft.EntityFrameworkCore;
using TeamGames.Core.Games;
using TeamGames.Core.Rooms;

namespace TeamGames.Infrastructure.Rooms;

public sealed partial class RoomStore
{
    public sealed record QueuedGame(Guid GuestId, GameCommand Command);
    public sealed record GameReply(CommandResult? Result, RoomException? Error);
    // One transaction can persist a voting burst. No command is acknowledged before commit.
    public async Task<IReadOnlyList<GameReply>> PlayBatchAsync(Guid roomId, IReadOnlyList<QueuedGame> commands)
    {
        await writer.Gate.WaitAsync();
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync();
            var room = await database.Rooms.Include(r => r.Members).SingleOrDefaultAsync(r => r.Id == roomId)
                ?? throw new RoomException("room_missing", "That room was not found", 404);
            await PopulatePrompts(room);
            var guestIds = commands.Select(c => c.GuestId).Distinct().ToArray();
            var commandIds = commands.Select(c => c.Command.CommandId).Distinct().ToArray();
            var expiries = await database.Guests.AsNoTracking().Where(g => guestIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.ExpiresTicks);
            var receipts = (await database.Receipts.AsNoTracking().Where(r => guestIds.Contains(r.GuestId) && commandIds.Contains(r.CommandId)).ToArrayAsync())
                .ToDictionary(r => (r.GuestId, r.CommandId));
            var accepted = new (Membership? Actor, long Version, bool Replayed, RoomException? Error)[commands.Count];
            for (var i = 0; i < commands.Count; i++)
            {
                var request = commands[i];
                var previous = (room.GameState, room.Status, room.Version, room.LastActivityTicks);
                try
                {
                    ValidateId(request.Command.CommandId);
                    if (!expiries.TryGetValue(request.GuestId, out var expiry) || expiry <= Now)
                        throw new RoomException("membership_revoked", "Your guest session has expired", 401);
                    var actor = Access(room, request.GuestId);
                    var fingerprint = Fingerprint(roomId.ToString(), request.Command);
                    if (receipts.TryGetValue((request.GuestId, request.Command.CommandId), out var receipt))
                    {
                        if (receipt.Fingerprint != fingerprint) throw new RoomException("command_reused", "That command ID was already used for a different request", 409);
                        accepted[i] = (actor, receipt.AppliedVersion, true, null);
                    }
                    else
                    {
                        games.Apply(room, actor, request.Command, Now);
                        receipt = new() { GuestId = request.GuestId, CommandId = request.Command.CommandId, RoomId = room.Id, Fingerprint = fingerprint, AppliedVersion = room.Version };
                        receipts.Add((request.GuestId, request.Command.CommandId), receipt);
                        database.Receipts.Add(receipt);
                        accepted[i] = (actor, room.Version, false, null);
                    }
                }
                catch (RoomException error)
                {
                    (room.GameState, room.Status, room.Version, room.LastActivityTicks) = previous;
                    accepted[i] = (null, 0, false, error);
                }
            }
            await database.SaveChangesAsync();
            await transaction.CommitAsync();
            // Every actor gets a fresh private view of the committed final batch state.
            var online = presence.Read(roomId);
            var participants = room.Members.Where(m => !m.Removed).OrderBy(m => m.Name, StringComparer.Ordinal)
                .Select(m => new ParticipantView(m.Id, m.Name, m.Id == room.FacilitatorId, online.Online.Contains(m.Id))).ToArray();
            var views = new Dictionary<Guid, RoomSnapshot>();
            return accepted.Select(outcome =>
            {
                if (outcome.Error is not null) return new GameReply(null, outcome.Error);
                var actor = outcome.Actor!;
                if (!views.TryGetValue(actor.Id, out var snapshot))
                {
                    snapshot = new(1, room.Id, room.Code, room.Version, online.Revision, presence.Epoch, room.Status, room.JoinLocked,
                        settings.Capacity, actor.Id, room.FacilitatorId, participants, RoomRules.Actions(room, actor), games.Project(room, actor));
                    views.Add(actor.Id, snapshot);
                }
                return new GameReply(new(snapshot, outcome.Version, outcome.Replayed), null);
            }).ToArray();
        }
        finally { writer.Gate.Release(); }
    }
}
