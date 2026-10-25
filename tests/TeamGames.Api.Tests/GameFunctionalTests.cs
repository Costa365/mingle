using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamGames.Core.Games;
using TeamGames.Core.Rooms;
using TeamGames.Infrastructure.Persistence;
using Browser = TeamGames.Api.Tests.RoomFunctionalTests.Browser;

namespace TeamGames.Api.Tests;

[Trait("Layer", "Functional")]
public sealed class GameFunctionalTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "mingle-game-tests-" + Guid.NewGuid().ToString("N"));
    private static GameCommand Command(RoomSnapshot snapshot, string action, string? choice = null) =>
        new(Guid.NewGuid(), action, snapshot.Game?.SessionId, snapshot.Game?.RoundId,
            snapshot.Game?.StageRevision ?? 0, snapshot.Game?.ResponseRevision ?? 0, choice, "this-or-that", snapshot.Version);
    private static async Task<CommandResult> Play(Browser browser, RoomSnapshot snapshot, string action, string? choice = null)
    {
        using var response = await browser.Post($"/api/rooms/{snapshot.Id}/game", Command(snapshot, action, choice));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CommandResult>())!;
    }
    private static async Task<RoomErrorView> Error(Browser browser, RoomSnapshot snapshot, GameCommand command, HttpStatusCode status)
    {
        using var response = await browser.Post($"/api/rooms/{snapshot.Id}/game", command);
        Assert.Equal(status, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RoomErrorView>())!;
    }
    [Fact]
    public async Task ThreePlayersCompleteRoundsWithConcurrentAnswersPrivateHubViewsAndSafeRetries()
    {
        using var factory = new ApiFactory(directory);
        using var host = await Browser.Create(factory);
        using var one = await Browser.Create(factory);
        using var two = await Browser.Create(factory);
        var room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
        await one.Enter(new(Guid.NewGuid(), "One", room.Code), false);
        await two.Enter(new(Guid.NewGuid(), "Two", room.Code), false);
        room = (await Play(host, await host.Snapshot(room.Id), "start")).Snapshot;
        await using var hub = host.Hub(factory);
        var snapshots = Channel.CreateUnbounded<RoomSnapshot>();
        hub.On<JsonElement>("Snapshot", json =>
        {
            if (json.TryGetProperty("game", out var game) && game.ValueKind == JsonValueKind.Object) AssertPublicGame(game);
            snapshots.Writer.TryWrite(json.Deserialize<RoomSnapshot>(JsonSerializerOptions.Web)!);
        });
        await hub.StartAsync();
        await hub.InvokeAsync("Subscribe", room.Id);
        var first = await one.Snapshot(room.Id);
        var second = await two.Snapshot(room.Id);
        var results = await Task.WhenAll(Play(one, first, "answer", "a"), Play(two, second, "answer", "b"));
        Assert.All(results, r => Assert.False(r.Replayed));
        var hidden = await Until(snapshots.Reader, s => s.Game?.ResponseCount == 2);
        Assert.Null(hidden.Game!.OwnChoice);
        Assert.Empty(hidden.Game.Results);
        Assert.Equal(3, hidden.Game.Responses!.Count);
        Assert.Equal(2, hidden.Game.Responses!.Count(r => r.HasResponded));
        Assert.False(hidden.Game.Responses!.Single(r => r.ParticipantId == room.YouId).HasResponded);
        using var raw = JsonDocument.Parse(await host.Client.GetStringAsync($"/api/rooms/{room.Id}"));
        AssertPublicGame(raw.RootElement.GetProperty("game"));
        foreach (var status in raw.RootElement.GetProperty("game").GetProperty("responses").EnumerateArray())
            Assert.Equal(new[] { "hasResponded", "participantId" }, status.EnumerateObject().Select(p => p.Name).Order());
        var playerView = await one.Snapshot(room.Id);
        Assert.Equal("a", playerView.Game!.OwnChoice);
        var changed = (await Play(one, playerView, "answer", "pass")).Snapshot;
        Assert.Equal(2, changed.Game!.ResponseRevision);
        Assert.Equal(hidden.Game.Responses, changed.Game.Responses);
        Assert.Equal(hidden.Game.Responses, (await host.Snapshot(room.Id)).Game!.Responses);
        var staleAnswer = Command(changed, "answer", "a");
        var reveal = Command(room, "reveal"); // Other answers do not invalidate a stage command.
        using var revealedResponse = await host.Post($"/api/rooms/{room.Id}/game", reveal);
        revealedResponse.EnsureSuccessStatusCode();
        var revealed = (await revealedResponse.Content.ReadFromJsonAsync<CommandResult>())!.Snapshot;
        var result = Assert.Single(revealed.Game!.Results);
        Assert.Equal((0, 1, 1, 1, 100), (result.A, result.B, result.Passes, result.Missing, result.PercentB));
        Assert.Equal("missing", result.Choices!.Single(c => c.Name == "Host").Choice);
        Assert.Equal("pass", result.Choices!.Single(c => c.Name == "One").Choice);
        Assert.Equal("b", result.Choices!.Single(c => c.Name == "Two").Choice);
        var revealedHub = await Until(snapshots.Reader, s => s.Game?.Stage == "revealed");
        Assert.Equal("b", revealedHub.Game!.Results[0].Choices!.Single(c => c.Name == "Two").Choice);
        Assert.Equal("stage_changed", (await Error(one, changed, staleAnswer, HttpStatusCode.Conflict)).Code);
        using var retryResponse = await host.Post($"/api/rooms/{room.Id}/game", reveal);
        var retry = (await retryResponse.Content.ReadFromJsonAsync<CommandResult>())!;
        Assert.True(retry.Replayed);
        Assert.Equal(revealed.Version, retry.AppliedVersion);
        Assert.Equal(revealed.Game.StageRevision, retry.Snapshot.Game!.StageRevision);
        await Error(host, room, reveal with { Action = "skip" }, HttpStatusCode.Conflict);
        var next = Command(revealed, "next");
        var attempts = await Task.WhenAll(host.Post($"/api/rooms/{room.Id}/game", next), host.Post($"/api/rooms/{room.Id}/game", next));
        Assert.All(attempts, response => response.EnsureSuccessStatusCode());
        foreach (var response in attempts) response.Dispose();
        room = await host.Snapshot(room.Id);
        Assert.Equal(2, room.Game!.RoundNumber);
        room = (await Play(host, room, "skip")).Snapshot;
        Assert.True(room.Game!.Results[1].Skipped);
        room = (await Play(host, room, "next")).Snapshot;
        room = (await Play(host, room, "reveal")).Snapshot;
        Assert.Equal(3, room.Game!.Results[2].Missing);
        room = (await Play(host, room, "finish")).Snapshot;
        Assert.Equal("finished", room.Game!.Stage);
        room = (await Play(host, room, "lobby")).Snapshot;
        Assert.Equal("lobby", room.Status);
        Assert.Null(room.Game);
    }
    [Fact]
    public async Task LateJoinRemovalTransferAndClosedRoomEnforceCurrentMembershipAndFrozenResults()
    {
        using var factory = new ApiFactory(directory);
        using var host = await Browser.Create(factory);
        using var player = await Browser.Create(factory);
        using var late = await Browser.Create(factory);
        using var outsider = await Browser.Create(factory);
        var room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
        var playerRoom = (await player.Enter(new(Guid.NewGuid(), "Player", room.Code), false)).Snapshot;
        room = (await Play(host, await host.Snapshot(room.Id), "start")).Snapshot;
        var answer = Command(await player.Snapshot(room.Id), "answer", "a");
        await Error(outsider, room, Command(room, "answer", "b"), HttpStatusCode.Forbidden);
        foreach (var action in new[] { "reveal", "skip", "next", "finish", "lobby", "start" })
            await Error(player, room, Command(room, action), HttpStatusCode.Forbidden);
        await Error(player, room, answer with { Choice = "bad" }, HttpStatusCode.BadRequest);
        using var submitted = await player.Post($"/api/rooms/{room.Id}/game", answer);
        submitted.EnsureSuccessStatusCode();
        await Error(player, room, answer with { CommandId = Guid.NewGuid() }, HttpStatusCode.Conflict);
        var lateRoom = (await late.Enter(new(Guid.NewGuid(), "Late", room.Code), false)).Snapshot;
        Assert.False(lateRoom.Game!.Eligible);
        Assert.DoesNotContain(lateRoom.Game.Responses!, r => r.ParticipantId == lateRoom.YouId);
        await Error(late, lateRoom, Command(lateRoom, "answer", "b"), HttpStatusCode.Forbidden);
        room = (await host.Command(room.Id, new(Guid.NewGuid(), (await host.Snapshot(room.Id)).Version, "remove", playerRoom.YouId))).Snapshot;
        await Error(player, room, answer, HttpStatusCode.Forbidden); // Receipt cannot restore revoked access.
        Assert.DoesNotContain(room.Game!.Responses!, r => r.ParticipantId == playerRoom.YouId);
        room = (await Play(host, room, "reveal")).Snapshot;
        var result = Assert.Single(room.Game!.Results);
        Assert.Equal((0, 1), (result.A, result.Missing));
        room = (await host.Command(room.Id, new(Guid.NewGuid(), room.Version, "transfer", lateRoom.YouId))).Snapshot;
        await Error(host, room, Command(room, "next"), HttpStatusCode.Forbidden);
        var lateView = await late.Snapshot(room.Id);
        room = (await Play(late, lateView, "next")).Snapshot;
        Assert.True(room.Game!.Eligible);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(Assert.Single(room.Game.Results)));
        room = (await late.Command(room.Id, new(Guid.NewGuid(), room.Version, "close"))).Snapshot;
        await Error(late, room, Command(room, "answer", "a"), HttpStatusCode.Gone);
    }
    [Fact]
    public async Task RestartRestoresPrivateAnswerStageAndReceiptThenFinalizedSummary()
    {
        CookieContainer cookies;
        RoomSnapshot room;
        GameCommand answer;
        using (var factory = new ApiFactory(directory))
        using (var host = await Browser.Create(factory))
        {
            room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
            room = (await Play(host, room, "start")).Snapshot;
            cookies = host.Cookies;
            answer = Command(room, "answer", "b");
            using var response = await host.Post($"/api/rooms/{room.Id}/game", answer);
            response.EnsureSuccessStatusCode();
            room = (await response.Content.ReadFromJsonAsync<CommandResult>())!.Snapshot;
        }
        using (var factory = new ApiFactory(directory))
        using (var host = await Browser.Create(factory, cookies))
        {
            var restored = await host.Snapshot(room.Id);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(room.Game), System.Text.Json.JsonSerializer.Serialize(restored.Game));
            using var retryResponse = await host.Post($"/api/rooms/{room.Id}/game", answer);
            Assert.True((await retryResponse.Content.ReadFromJsonAsync<CommandResult>())!.Replayed);
            room = (await Play(host, restored, "reveal")).Snapshot;
            room = (await Play(host, room, "finish")).Snapshot;
        }
        using (var factory = new ApiFactory(directory))
        using (var host = await Browser.Create(factory, cookies))
        {
            var restored = await host.Snapshot(room.Id);
            Assert.Equal("finished", restored.Game!.Stage);
            Assert.Equal(1, Assert.Single(restored.Game.Results).B);
            await Play(host, restored, "lobby");
            await using var scope = factory.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<TeamGamesDbContext>();
            Assert.NotNull((await database.Rooms.SingleAsync()).GameState);
        }
    }
    [Fact]
    public async Task GameMutationRequiresCookieCsrfOriginValidIdAndFreshRound()
    {
        using var factory = new ApiFactory(directory);
        using var host = await Browser.Create(factory);
        var room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
        var start = Command(room, "start");
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync($"/api/rooms/{room.Id}/game", start)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsJsonAsync($"/api/rooms/{room.Id}/game", start)).StatusCode);
        host.Client.DefaultRequestHeaders.Add("Origin", "https://elsewhere.example");
        await Error(host, room, start, HttpStatusCode.Forbidden);
        host.Client.DefaultRequestHeaders.Remove("Origin");
        await Error(host, room, start with { CommandId = Guid.Empty }, HttpStatusCode.BadRequest);
        await Error(host, room, start with { GameId = "unknown" }, HttpStatusCode.BadRequest);
        room = (await Play(host, room, "start")).Snapshot;
        await Error(host, room, Command(room, "reveal") with { RoundId = Guid.NewGuid() }, HttpStatusCode.Conflict);
        await Error(host, room, Command(room, "reveal") with { SessionId = Guid.NewGuid() }, HttpStatusCode.Conflict);
        await Error(host, room, Command(room, "next"), HttpStatusCode.Conflict);
    }
    [Fact]
    public async Task RevealedResultsSurviveRemovalAndPlayingRoomsExpireAndArePurged()
    {
        var clock = new ManualClock();
        using var factory = new ApiFactory(directory, clock: clock);
        using var host = await Browser.Create(factory);
        using var player = await Browser.Create(factory);
        var room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
        var joined = (await player.Enter(new(Guid.NewGuid(), "Player", room.Code), false)).Snapshot;
        room = (await Play(host, await host.Snapshot(room.Id), "start")).Snapshot;
        await Play(player, await player.Snapshot(room.Id), "answer", "a");
        room = (await Play(host, room, "reveal")).Snapshot;
        var result = Assert.Single(room.Game!.Results);
        room = (await host.Command(room.Id, new(Guid.NewGuid(), room.Version, "remove", joined.YouId))).Snapshot;
        var preserved = Assert.Single(room.Game!.Results);
        Assert.Equal(result.A, preserved.A);
        Assert.Equal(result.Missing, preserved.Missing);
        Assert.Equal("Removed player", preserved.Choices!.Single(c => c.ParticipantId == joined.YouId).Name);
        Assert.Equal(1, result.A);
        clock.Advance(TimeSpan.FromHours(25));
        Assert.Equal(HttpStatusCode.Gone, (await host.Client.GetAsync($"/api/rooms/{room.Id}")).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<TeamGames.Infrastructure.Rooms.RoomStore>();
        await store.SweepAsync();
        clock.Advance(TimeSpan.FromHours(25));
        await store.SweepAsync();
        var database = scope.ServiceProvider.GetRequiredService<TeamGamesDbContext>();
        Assert.Empty(await database.Rooms.ToArrayAsync());
        Assert.Empty(await database.Receipts.ToArrayAsync());
    }
    [Fact]
    public async Task AggregateOnlySavedGamesRemainReadableWithoutInventingNamedChoices()
    {
        CookieContainer cookies;
        RoomSnapshot room;
        using (var factory = new ApiFactory(directory))
        using (var host = await Browser.Create(factory))
        {
            cookies = host.Cookies;
            room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
            room = (await Play(host, room, "start")).Snapshot;
            room = (await Play(host, room, "answer", "a")).Snapshot;
            room = (await Play(host, room, "reveal")).Snapshot;
        }
        await using (var database = new TeamGamesDbContext(new DbContextOptionsBuilder<TeamGamesDbContext>()
            .UseSqlite($"Data Source={Path.Combine(directory, "teamgames.db")}").Options))
        {
            var entity = await database.Rooms.SingleAsync();
            var saved = System.Text.Json.Nodes.JsonNode.Parse(entity.GameState!)!;
            saved["SchemaVersion"] = 1;
            var session = saved["Sessions"]![0]!;
            var state = System.Text.Json.Nodes.JsonNode.Parse(session["State"]!.GetValue<string>())!;
            foreach (var result in state["Results"]!.AsArray()) result!.AsObject().Remove("Choices");
            session["State"] = state.ToJsonString();
            entity.GameState = saved.ToJsonString();
            await database.SaveChangesAsync();
        }
        using (var factory = new ApiFactory(directory))
        using (var host = await Browser.Create(factory, cookies))
        {
            room = await host.Snapshot(room.Id);
            Assert.Equal(1, Assert.Single(room.Game!.Results).A);
            Assert.Empty(room.Game.Results[0].Choices!);
            room = (await Play(host, room, "next")).Snapshot;
            room = (await Play(host, room, "reveal")).Snapshot;
            Assert.Equal("missing", Assert.Single(room.Game!.Results[1].Choices!).Choice);
        }
    }
    private static void AssertPublicGame(JsonElement game)
    {
        string[] allowed = ["sessionId", "gameId", "roundId", "roundNumber", "stageRevision", "stage", "prompt", "optionA", "optionB",
            "eligibleCount", "responseCount", "eligible", "ownChoice", "responseRevision", "allowedActions", "results", "remainingPrompts", "poolSize", "category", "tag", "search", "usedPromptIds", "responses"];
        Assert.Equal(allowed.Order(), game.EnumerateObject().Select(p => p.Name).Order());
    }
    private static async Task<RoomSnapshot> Until(ChannelReader<RoomSnapshot> reader, Func<RoomSnapshot, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true) { var next = await reader.ReadAsync(timeout.Token); if (predicate(next)) return next; }
    }
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
