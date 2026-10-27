using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TeamGames.Api.Rooms;
using TeamGames.Core.Games;
using TeamGames.Core.Rooms;
using TeamGames.Infrastructure.Persistence;
using TeamGames.Infrastructure.Rooms;
using Browser = TeamGames.Api.Tests.RoomFunctionalTests.Browser;

namespace TeamGames.Api.Tests;

[Trait("Layer", "Functional")]
public sealed class ResilienceFunctionalTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "mingle-resilience-" + Guid.NewGuid().ToString("N"));
    private sealed class FailOnceLifetime(ILogger<DefaultHubLifetimeManager<RoomHub>> logger) : DefaultHubLifetimeManager<RoomHub>(logger)
    {
        public int Remaining;
        public int Dropped;
        public Guid RoomId;
        public long AfterVersion;
        public override Task SendConnectionAsync(string connectionId, string methodName, object?[] args, CancellationToken cancellationToken = default)
        {
            if (methodName == "Snapshot" && args.FirstOrDefault() is RoomSnapshot snapshot && snapshot.Id == RoomId && snapshot.Version > AfterVersion && Interlocked.Exchange(ref Remaining, 0) > 0) { Dropped++; throw new IOException("Synthetic notification failure"); }
            return base.SendConnectionAsync(connectionId, methodName, args, cancellationToken);
        }
    }
    [Fact]
    public async Task ARealHubDeliveryFailureCannotLoseAnAcknowledgedResponseOrItsRetryReceipt()
    {
        System.Net.CookieContainer cookies; RoomSnapshot room; GameCommand answer;
        using (var factory = new ApiFactory(directory, configureServices: services =>
        {
            services.RemoveAll<HubLifetimeManager<RoomHub>>();
            services.AddSingleton<HubLifetimeManager<RoomHub>, FailOnceLifetime>();
        }))
        using (var host = await Browser.Create(factory))
        {
            cookies = host.Cookies; room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
            using var started = await host.Post($"/api/rooms/{room.Id}/game", new GameCommand(Guid.NewGuid(), "start", GameId: "this-or-that", ExpectedVersion: room.Version));
            started.EnsureSuccessStatusCode(); room = (await started.Content.ReadFromJsonAsync<CommandResult>())!.Snapshot;
            await using var hub = host.Hub(factory); await hub.StartAsync(); await hub.InvokeAsync("Subscribe", room.Id);
            var failure = (FailOnceLifetime)factory.Services.GetRequiredService<HubLifetimeManager<RoomHub>>(); failure.RoomId = room.Id; failure.AfterVersion = room.Version; failure.Remaining = 1;
            var g = room.Game!; answer = new(Guid.NewGuid(), "answer", g.SessionId, g.RoundId, g.StageRevision, Choice: "a");
            using var accepted = await host.Post($"/api/rooms/{room.Id}/game", answer); accepted.EnsureSuccessStatusCode();
            Assert.Equal(1, failure.Dropped);
            Assert.Equal("a", (await host.Snapshot(room.Id)).Game!.OwnChoice);
        }
        using (var restarted = new ApiFactory(directory))
        using (var host = await Browser.Create(restarted, cookies))
        {
            using var retry = await host.Post($"/api/rooms/{room.Id}/game", answer); retry.EnsureSuccessStatusCode();
            var result = (await retry.Content.ReadFromJsonAsync<CommandResult>())!;
            Assert.True(result.Replayed); Assert.Equal("a", result.Snapshot.Game!.OwnChoice);
            Assert.Equal(room.Version + 1, result.Snapshot.Version);
        }
    }
    [Fact]
    public async Task BatchedViewsKeepPrivateSubmissionsSeparateAndRecheckRemovedAndExpiredGuests()
    {
        using var factory = new ApiFactory(directory);
        using var host = await Browser.Create(factory); using var player = await Browser.Create(factory); using var outsider = await Browser.Create(factory);
        var room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
        room = (await player.Enter(new(Guid.NewGuid(), "Player", room.Code), false)).Snapshot;
        var other = (await outsider.Enter(new(Guid.NewGuid(), "Outside"))).Snapshot;
        using var started = await host.Post($"/api/rooms/{room.Id}/game", new GameCommand(Guid.NewGuid(), "start", GameId: "two-truths-and-a-lie", ExpectedVersion: room.Version));
        room = (await started.Content.ReadFromJsonAsync<CommandResult>())!.Snapshot;
        var g = room.Game!;
        using var submitted = await player.Post($"/api/rooms/{room.Id}/game", new GameCommand(Guid.NewGuid(), "submit", g.SessionId, g.RoundId, g.StageRevision, Submission: new(["Private one", "Private two", "Private three"], 2)));
        submitted.EnsureSuccessStatusCode(); room = await host.Snapshot(room.Id);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TeamGamesDbContext>(); var store = scope.ServiceProvider.GetRequiredService<RoomStore>();
        var hostId = (await db.Memberships.AsNoTracking().SingleAsync(m => m.Id == room.YouId)).GuestId;
        var playerMember = await db.Memberships.AsNoTracking().SingleAsync(m => m.RoomId == room.Id && m.Name == "Player");
        var outsiderId = (await db.Memberships.AsNoTracking().SingleAsync(m => m.Id == other.YouId)).GuestId;
        var batch = await store.GetManyAsync(room.Id, [hostId, playerMember.GuestId, outsiderId]);
        Assert.Null(batch[hostId].Snapshot!.Game!.OwnSubmission);
        Assert.Equal(2, batch[playerMember.GuestId].Snapshot!.Game!.OwnSubmission!.LieIndex);
        Assert.Null(batch[outsiderId].Snapshot); Assert.Equal(403, batch[outsiderId].Error!.Status);
        await host.Command(room.Id, new(Guid.NewGuid(), room.Version, "remove", playerMember.Id));
        batch = await store.GetManyAsync(room.Id, [hostId, playerMember.GuestId]);
        Assert.Equal(403, batch[playerMember.GuestId].Error!.Status);
        await db.Guests.Where(p => p.Id == hostId).ExecuteUpdateAsync(update => update.SetProperty(p => p.ExpiresTicks, 0));
        Assert.Equal(401, (await store.GetManyAsync(room.Id, [hostId]))[hostId].Error!.Status);
    }
    [Fact]
    public async Task ConcurrentBatchReplaysInvalidInputsAndOutsidersCannotCorruptAcceptedResponses()
    {
        using var factory = new ApiFactory(directory);
        using var host = await Browser.Create(factory); using var player = await Browser.Create(factory); using var outsider = await Browser.Create(factory);
        var room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
        room = (await player.Enter(new(Guid.NewGuid(), "Player", room.Code), false)).Snapshot;
        using var start = await host.Post($"/api/rooms/{room.Id}/game", new GameCommand(Guid.NewGuid(), "start", GameId: "this-or-that", ExpectedVersion: room.Version));
        room = (await start.Content.ReadFromJsonAsync<CommandResult>())!.Snapshot; var g = room.Game!;
        var answer = new GameCommand(Guid.NewGuid(), "answer", g.SessionId, g.RoundId, g.StageRevision, Choice: "a");
        var replies = await Task.WhenAll(host.Post($"/api/rooms/{room.Id}/game", answer), host.Post($"/api/rooms/{room.Id}/game", answer),
            player.Post($"/api/rooms/{room.Id}/game", answer with { CommandId = Guid.NewGuid(), Choice = "b" }),
            player.Post($"/api/rooms/{room.Id}/game", answer with { CommandId = Guid.NewGuid(), Choice = "invalid" }),
            outsider.Post($"/api/rooms/{room.Id}/game", answer with { CommandId = Guid.NewGuid() }));
        Assert.Equal(System.Net.HttpStatusCode.OK, replies[0].StatusCode); Assert.Equal(System.Net.HttpStatusCode.OK, replies[1].StatusCode);
        var first = (await replies[0].Content.ReadFromJsonAsync<CommandResult>())!; var second = (await replies[1].Content.ReadFromJsonAsync<CommandResult>())!;
        Assert.NotEqual(first.Replayed, second.Replayed); Assert.Equal(first.AppliedVersion, second.AppliedVersion);
        Assert.Equal(System.Net.HttpStatusCode.OK, replies[2].StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, replies[3].StatusCode); Assert.Equal(System.Net.HttpStatusCode.Forbidden, replies[4].StatusCode);
        var after = await host.Snapshot(room.Id); Assert.Equal(room.Version + 2, after.Version); Assert.Equal(2, after.Game!.ResponseCount);
        Assert.Equal("a", after.Game.OwnChoice); Assert.Equal("b", (await player.Snapshot(room.Id)).Game!.OwnChoice);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, (await host.Post($"/api/rooms/{room.Id}/game", answer with { Choice = "b" })).StatusCode);
        foreach (var reply in replies) reply.Dispose();
    }
    [Fact]
    public async Task ARealSqliteFailureRollsBackTheEntireBatchAndSameIdsCanSafelyRetry()
    {
        using var factory = new ApiFactory(directory);
        using var host = await Browser.Create(factory); using var player = await Browser.Create(factory);
        var room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
        room = (await player.Enter(new(Guid.NewGuid(), "Player", room.Code), false)).Snapshot;
        using var start = await host.Post($"/api/rooms/{room.Id}/game", new GameCommand(Guid.NewGuid(), "start", GameId: "this-or-that", ExpectedVersion: room.Version));
        room = (await start.Content.ReadFromJsonAsync<CommandResult>())!.Snapshot; var g = room.Game!;
        var a = new GameCommand(Guid.NewGuid(), "answer", g.SessionId, g.RoundId, g.StageRevision, Choice: "a");
        var b = a with { CommandId = Guid.NewGuid(), Choice = "b" };
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TeamGamesDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER synthetic_failure BEFORE INSERT ON Receipts BEGIN SELECT RAISE(ABORT, 'synthetic receipt failure'); END;");
        var failed = await Task.WhenAll(host.Post($"/api/rooms/{room.Id}/game", a), player.Post($"/api/rooms/{room.Id}/game", b));
        Assert.All(failed, response => Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode));
        var unchanged = await host.Snapshot(room.Id); Assert.Equal(room.Version, unchanged.Version); Assert.Equal(0, unchanged.Game!.ResponseCount);
        Assert.False(await db.Receipts.AnyAsync(r => r.CommandId == a.CommandId || r.CommandId == b.CommandId));
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER synthetic_failure;");
        var retried = await Task.WhenAll(host.Post($"/api/rooms/{room.Id}/game", a), player.Post($"/api/rooms/{room.Id}/game", b));
        Assert.All(retried, response => Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(room.Version + 2, (await host.Snapshot(room.Id)).Version);
        using var duplicate = await host.Post($"/api/rooms/{room.Id}/game", a);
        Assert.True((await duplicate.Content.ReadFromJsonAsync<CommandResult>())!.Replayed);
        foreach (var response in failed.Concat(retried)) response.Dispose();
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
