using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamGames.Core.Administration;
using TeamGames.Core.Rooms;
using TeamGames.Core.Prompts;
using TeamGames.Infrastructure.Administration;

namespace TeamGames.Infrastructure.Rooms;

public sealed partial class RoomStore
{
    private AdminRoomView AdminView(Room room) => new(room.Id, room.Code, room.Version,
        RoomRules.IsExpired(room, Now, settings) ? "expired" : room.Status, room.JoinLocked,
        room.Members.Count(m => !m.Removed), room.LastActivityTicks, room.FacilitatorId,
        room.Members.Where(m => !m.Removed).OrderBy(m => m.Name).Select(m => new ParticipantView(m.Id, m.Name, m.Id == room.FacilitatorId, presence.Read(room.Id).Online.Contains(m.Id))).ToArray(),
        games.Describe(room));
    public async Task<AdminRoomPage> AdminRoomsAsync(int page, int pageSize, string? status)
    {
        AdminRules.Pagination(page, pageSize);
        if (status is not (null or "" or "lobby" or "playing" or "closed" or "expired")) throw new RoomException("invalid_filter", "Choose a room status");
        var query = database.Rooms.AsNoTracking();
        if (!string.IsNullOrEmpty(status)) query = query.Where(r => r.Status == status);
        var total = await query.CountAsync();
        var rooms = await query.Include(r => r.Members).OrderByDescending(r => r.LastActivityTicks).ThenBy(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync();
        return new(page, pageSize, total, rooms.Select(AdminView).ToArray());
    }
    public async Task<AdminRoomView> AdminRoomAsync(Guid id) => AdminView(await Load(id));
    private async Task AuthorizeAdmin(Guid sessionId, AdminSettings configuration)
    {
        if (!configuration.Enabled || !await database.AdminSessions.AnyAsync(s => s.Id == sessionId && s.ExpiresTicks > Now && s.CredentialVersion == configuration.CredentialVersion))
            throw new RoomException("admin_required", "Sign in as an administrator", 401);
    }
    private async Task<AdminAudit?> AdminReceipt(Guid sessionId, Guid commandId, string fingerprint)
    {
        ValidateId(commandId);
        var receipt = await database.AdminAudits.SingleOrDefaultAsync(a => a.SessionId == sessionId && a.CommandId == commandId);
        if (receipt is not null && receipt.Fingerprint != fingerprint) throw new RoomException("command_reused", "That command ID was already used", 409);
        return receipt;
    }
    private void Audit(Guid sessionId, Guid commandId, string action, string target, string fingerprint, long version) =>
        database.AdminAudits.Add(new AdminAudit
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            CommandId = commandId,
            CreatedTicks = Now,
            Action = action,
            TargetId = target,
            Fingerprint = fingerprint,
            AppliedVersion = version
        });
    public async Task<AdminCommandResult> AdminExecuteAsync(Guid roomId, Guid sessionId, AdminSettings configuration, AdminRoomCommand command)
    {
        await writer.Gate.WaitAsync();
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync();
            await AuthorizeAdmin(sessionId, configuration);
            var fingerprint = Fingerprint("admin-room:" + roomId, command);
            var receipt = await AdminReceipt(sessionId, command.CommandId, fingerprint);
            if (receipt is not null) return new(receipt.AppliedVersion, true);
            var room = await database.Rooms.Include(r => r.Members).SingleOrDefaultAsync(r => r.Id == roomId)
                ?? throw new RoomException("room_missing", "That room was not found", 404);
            if (room.Version != command.ExpectedVersion) throw new RoomException("version_changed", "The room changed; refresh and review it", 409);
            if (command.Action == "delete")
            {
                AdminRules.ConfirmDeletion(room, command);
                database.Rooms.Remove(room);
            }
            else if (command.Action == "end-game")
            {
                if (RoomRules.IsExpired(room, Now, settings)) throw new RoomException("room_expired", "This room has expired", 410);
                games.FinishAdministrative(room, Now);
            }
            else if (RoomRules.IsExpired(room, Now, settings)) throw new RoomException("room_expired", "This room has expired", 410);
            else RoomRules.ApplyManagement(room, new(command.CommandId, command.ExpectedVersion, command.Action, command.TargetId, command.Locked), Now);
            Audit(sessionId, command.CommandId, "room-" + command.Action, roomId.ToString(), fingerprint, room.Version);
            await database.SaveChangesAsync();
            await transaction.CommitAsync();
            if (command.Action == "delete") promptCache.Remove(roomId);
            return new(room.Version, false);

        }
        finally { writer.Gate.Release(); }
    }
    public async Task<AdminPromptPage> AdminPromptsAsync(int page, int pageSize, string? gameId, string? search)
    {
        AdminRules.Pagination(page, pageSize);
        if (gameId is not (null or "") && !PromptRules.GameIds.Contains(gameId) || search?.Length > 120) throw new RoomException("invalid_filter", "Choose a supported game and short search");
        var query = database.Prompts.AsNoTracking().Where(p => p.RoomId == null);
        if (!string.IsNullOrEmpty(gameId)) query = query.Where(p => p.GameId == gameId);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(p => p.Text.Contains(search) || p.OptionA.Contains(search) || p.OptionB.Contains(search) || p.Category.Contains(search));
        var total = await query.CountAsync();
        var prompts = await query.OrderBy(p => p.GameId).ThenBy(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync();
        return new(page, pageSize, total, prompts.Select(p => PromptRules.View(p, false)).ToArray());
    }
    public async Task<AdminCommandResult> AdminPromptAsync(Guid sessionId, AdminSettings configuration, AdminPromptCommand command)
    {
        await writer.Gate.WaitAsync();
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync();
            await AuthorizeAdmin(sessionId, configuration);
            var fingerprint = Fingerprint("admin-prompt", command);
            var receipt = await AdminReceipt(sessionId, command.CommandId, fingerprint);
            if (receipt is not null) return new(receipt.AppliedVersion, true);
            Prompt? current = null;
            if (command.Action is "edit" or "delete" or "enable" or "disable")
            {
                current = await database.Prompts.SingleOrDefaultAsync(p => p.Id == command.PromptId && p.RoomId == null)
                    ?? throw new RoomException("prompt_missing", "That shared prompt was not found", 404);
                if (current.Revision != command.PromptRevision) throw new RoomException("prompt_changed", "The prompt changed; refresh and review it", 409);
            }
            var id = current?.Id ?? Guid.NewGuid().ToString();
            var revision = (current?.Revision ?? 0) + 1;
            if (command.Action == "delete")
            {
                if (current!.SeedFingerprint is not null) database.PromptTombstones.Add(new() { Id = id });
                database.Prompts.Remove(current);
            }
            else if (command.Action is "enable" or "disable") { current!.Enabled = command.Action == "enable"; current.Revision = revision; }
            else if (command.Action is "create" or "edit")
            {
                var candidate = PromptRules.Create(command.Input ?? throw new RoomException("invalid_prompt", "Enter prompt content"), id, null);
                candidate.Source = "administrator";
                candidate.Revision = revision;
                if (await database.Prompts.AnyAsync(p => p.Id != id && p.RoomId == null && p.NormalizedKey == candidate.NormalizedKey))
                    throw new RoomException("duplicate_prompt", "That shared prompt already exists", 409);
                if (current is null) database.Prompts.Add(candidate);
                else
                {
                    candidate.SeedFingerprint = current.SeedFingerprint;
                    database.Entry(current).CurrentValues.SetValues(candidate);
                }
            }
            else throw new RoomException("invalid_command", "That prompt action is not supported");
            // Invalidate connected facilitator libraries without advancing any game stage.
            await database.Rooms.Where(r => r.Status == "lobby" || r.Status == "playing")
                .ExecuteUpdateAsync(update => update.SetProperty(r => r.Version, r => r.Version + 1));
            Audit(sessionId, command.CommandId, "prompt-" + command.Action, id, fingerprint, revision);
            await database.SaveChangesAsync();
            await transaction.CommitAsync();
            promptCache.Invalidate();
            return new(revision, false);
        }
        finally { writer.Gate.Release(); }
    }
    public Task<Guid[]> AdminActiveRoomIdsAsync() => database.Rooms.AsNoTracking()
        .Where(r => r.Status == "lobby" || r.Status == "playing").Select(r => r.Id).ToArrayAsync();
    public async Task<AdminAuditPage> AdminAuditAsync(int page, int pageSize)
    {
        AdminRules.Pagination(page, pageSize);
        return new(page, pageSize, await database.AdminAudits.CountAsync(), await database.AdminAudits.AsNoTracking().OrderByDescending(a => a.CreatedTicks).ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(a => new AdminAuditView(a.Id, a.CreatedTicks, a.Action, a.TargetId)).ToArrayAsync());
    }
}
