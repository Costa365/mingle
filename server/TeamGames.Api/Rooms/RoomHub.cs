using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TeamGames.Core.Rooms;
using TeamGames.Infrastructure.Rooms;

namespace TeamGames.Api.Rooms;

[Authorize]
public sealed class RoomHub(RoomStore store, PresenceRegistry presence, RoomPublisher publisher) : Hub
{
    public async Task Subscribe(Guid roomId)
    {
        try
        {
            var guestId = GuestAuthentication.GuestId(Context.User!);
            var snapshot = await store.GetAsync(roomId, guestId);
            if (snapshot.Status is not ("lobby" or "playing")) throw new RoomException("room_closed", "This room is closed", 410);
            var previousRoom = presence.Disconnect(Context.ConnectionId);
            presence.Subscribe(new(Context.ConnectionId, roomId, guestId, snapshot.YouId, Context.Abort));
            if (previousRoom is { } previous && previous != roomId) await publisher.PublishAsync(previous);
            await publisher.PublishAsync(roomId);
        }
        catch (RoomException exception) { throw new HubException(exception.Code); }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var roomId = presence.Disconnect(Context.ConnectionId);
        if (roomId is { } id) await publisher.PublishAsync(id);
        await base.OnDisconnectedAsync(exception);
    }
}

public class RoomPublisher(IServiceScopeFactory scopes, IHubContext<RoomHub> hub,
    PresenceRegistry presence, ILogger<RoomPublisher> logger)
{
    private sealed class Pending
    {
        public bool Dirty { get; set; } = true;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly object sync = new();
    private readonly Dictionary<Guid, Pending> pending = [];
    public virtual Task PublishAsync(Guid roomId)
    {
        lock (sync)
        {
            if (pending.TryGetValue(roomId, out var existing)) { existing.Dirty = true; return existing.Completion.Task; }
            var work = new Pending(); pending.Add(roomId, work);
            _ = Run(roomId, work);
            return work.Completion.Task;
        }
    }
    private async Task Run(Guid roomId, Pending work)
    {
        try
        {
            // Coalesce a burst of committed changes; never delay persistence or authorization.
            await Task.Delay(10);
            while (true)
            {
                lock (sync) work.Dirty = false;
                var subscribers = presence.Subscribers(roomId);
                if (subscribers.Length > 0)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var views = await scope.ServiceProvider.GetRequiredService<RoomStore>().GetManyAsync(roomId, subscribers.Select(s => s.GuestId).Distinct().ToArray());
                    await Task.WhenAll(subscribers.Select(async subscription =>
                    {
                        try
                        {
                            var delivery = views[subscription.GuestId];
                            if (delivery.Error is { } error)
                            {
                                presence.Disconnect(subscription.ConnectionId);
                                await hub.Clients.Client(subscription.ConnectionId).SendAsync("AccessEnded", new { code = error.Code });
                                subscription.Abort();
                            }
                            else
                            {
                                await hub.Clients.Client(subscription.ConnectionId).SendAsync("Snapshot", delivery.Snapshot!);
                                if (delivery.Snapshot!.Status == "closed") { presence.Disconnect(subscription.ConnectionId); subscription.Abort(); }
                            }
                        }
                        catch (Exception exception) { logger.LogWarning("Room notification failed ({ErrorType})", exception.GetType().Name); }
                    }));
                }
                lock (sync)
                {
                    if (work.Dirty) continue;
                    pending.Remove(roomId); work.Completion.TrySetResult(); return;
                }
            }
        }
        catch (Exception exception)
        {
            // Commit already succeeded. Reconciliation repairs a failed delivery.
            logger.LogWarning("Room notification failed ({ErrorType})", exception.GetType().Name);
            lock (sync) { pending.Remove(roomId); work.Completion.TrySetResult(); }
        }
    }
}

public sealed class RoomMaintenance(IServiceScopeFactory scopes, RoomPublisher publisher, PresenceRegistry presence,
    TimeProvider time, ILogger<RoomMaintenance> logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await Sweep();
        await base.StartAsync(cancellationToken);
    }

    private async Task Sweep()
    {
        await using var scope = scopes.CreateAsyncScope();
        var rooms = await scope.ServiceProvider.GetRequiredService<RoomStore>().SweepAsync();
        foreach (var id in rooms.Concat(presence.Sweep()).Distinct()) await publisher.PublishAsync(id);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2), time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await Sweep(); }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("Room maintenance failed ({ErrorType})", exception.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
