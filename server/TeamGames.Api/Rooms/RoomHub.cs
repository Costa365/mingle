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
            if (snapshot.Status != "lobby") throw new RoomException("room_closed", "This room is closed", 410);
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

public sealed class RoomPublisher(IServiceScopeFactory scopes, IHubContext<RoomHub> hub,
    PresenceRegistry presence, ILogger<RoomPublisher> logger)
{
    public async Task PublishAsync(Guid roomId)
    {
        foreach (var subscription in presence.Subscribers(roomId))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var snapshot = await scope.ServiceProvider.GetRequiredService<RoomStore>().GetAsync(roomId, subscription.GuestId);
                await hub.Clients.Client(subscription.ConnectionId).SendAsync("Snapshot", snapshot);
                if (snapshot.Status == "closed")
                {
                    presence.Disconnect(subscription.ConnectionId);
                    subscription.Abort();
                }
            }
            catch (RoomException exception)
            {
                presence.Disconnect(subscription.ConnectionId);
                await hub.Clients.Client(subscription.ConnectionId).SendAsync("AccessEnded", new { code = exception.Code });
                subscription.Abort();
            }
            catch (Exception exception)
            {
                // Commit already succeeded. Periodic reconciliation repairs delivery failures.
                logger.LogWarning("Room notification failed ({ErrorType})", exception.GetType().Name);
            }
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
