using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;
using TeamGames.Core.Games;
using TeamGames.Core.Rooms;
using Browser = TeamGames.Api.Tests.RoomFunctionalTests.Browser;

namespace TeamGames.Api.Tests;

[Trait("Layer", "Functional")]
public sealed class M4GameFunctionalTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "mingle-m4-" + Guid.NewGuid().ToString("N"));
    private static GameCommand Command(RoomSnapshot s, string action, string? choice = null, StatementSubmission? submission = null, string? id = null) =>
        new(Guid.NewGuid(), action, s.Game?.SessionId, s.Game?.RoundId, s.Game?.StageRevision ?? 0, s.Game?.ResponseRevision ?? 0, choice, id ?? s.Game?.GameId, s.Version, Submission: submission);
    private static async Task<RoomSnapshot> Play(Browser b, RoomSnapshot s, string action, string? choice = null, StatementSubmission? submission = null, string? id = null)
    {
        using var response = await b.Post($"/api/rooms/{s.Id}/game", Command(s, action, choice, submission, id));
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<CommandResult>())!.Snapshot;
    }
    private static async Task Denied(Browser b, RoomSnapshot s, GameCommand c, HttpStatusCode status)
    {
        using var response = await b.Post($"/api/rooms/{s.Id}/game", c); Assert.Equal(status, response.StatusCode);
    }
    [Theory]
    [InlineData("would-you-rather")]
    [InlineData("never-have-i-ever")]
    [InlineData("most-likely-to")]
    public async Task PromptModulesAuthorizeDeduplicateProjectPrivatelyAndSurviveRestart(string id)
    {
        CookieContainer hostCookies, playerCookies; RoomSnapshot room; GameCommand answer;
        using (var factory = new ApiFactory(directory))
        using (var host = await Browser.Create(factory))
        using (var player = await Browser.Create(factory))
        using (var outsider = await Browser.Create(factory))
        {
            room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
            var joined = (await player.Enter(new(Guid.NewGuid(), "Player", room.Code), false)).Snapshot;
            room = await Play(host, await host.Snapshot(room.Id), "start", id: id);
            await Denied(player, room, Command(room, "reveal"), HttpStatusCode.Forbidden);
            await Denied(outsider, room, Command(room, "answer", "pass"), HttpStatusCode.Forbidden);
            await Denied(player, joined, Command(await player.Snapshot(room.Id), "answer", "invalid"), HttpStatusCode.BadRequest);
            if (id == "most-likely-to") await Denied(player, room, Command(await player.Snapshot(room.Id), "answer", joined.YouId.ToString()), HttpStatusCode.BadRequest);
            await using var hub = player.Hub(factory);
            var snapshots = Channel.CreateUnbounded<RoomSnapshot>();
            hub.On<RoomSnapshot>("Snapshot", s => snapshots.Writer.TryWrite(s));
            await hub.StartAsync(); await hub.InvokeAsync("Subscribe", room.Id);
            answer = Command(room, "answer", id == "most-likely-to" ? joined.YouId.ToString() : "a");
            using var applied = await host.Post($"/api/rooms/{room.Id}/game", answer); applied.EnsureSuccessStatusCode();
            using var replay = await host.Post($"/api/rooms/{room.Id}/game", answer);
            Assert.True((await replay.Content.ReadFromJsonAsync<CommandResult>())!.Replayed);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            RoomSnapshot view; do { view = await snapshots.Reader.ReadAsync(timeout.Token); } while (view.Game?.ResponseCount != 1);
            Assert.Null(view.Game!.OwnChoice); Assert.Null(view.Game.OwnSubmission); Assert.Empty(view.Game.Results);
            room = await host.Snapshot(room.Id); hostCookies = host.Cookies; playerCookies = player.Cookies;
        }
        using (var factory = new ApiFactory(directory))
        using (var host = await Browser.Create(factory, hostCookies))
        using (var player = await Browser.Create(factory, playerCookies))
        {
            var restored = await host.Snapshot(room.Id);
            Assert.Equal(JsonSerializer.Serialize(room.Game), JsonSerializer.Serialize(restored.Game));
            using var retry = await host.Post($"/api/rooms/{room.Id}/game", answer);
            Assert.True((await retry.Content.ReadFromJsonAsync<CommandResult>())!.Replayed);
            var stale = Command(await player.Snapshot(room.Id), "answer", "pass");
            room = await Play(host, restored, "reveal");
            if (id == "most-likely-to") Assert.Empty(room.Game!.Results[0].Choices);
            else Assert.Equal("a", room.Game!.Results[0].Choices.Single(c => c.Name == "Host").Choice);
            await Denied(player, room, stale, HttpStatusCode.Conflict);
            room = await Play(host, room, "next"); room = await Play(host, room, "finish"); room = await Play(host, room, "lobby");
            Assert.Equal("lobby", room.Status);
        }
    }
    [Fact]
    public async Task PrivateCollectionGuessingAndRevealSurviveRestartsWithoutLeakingLie()
    {
        CookieContainer hc, pc; RoomSnapshot room; GameCommand submission;
        using (var factory = new ApiFactory(directory))
        using (var host = await Browser.Create(factory))
        using (var player = await Browser.Create(factory))
        {
            room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
            await player.Enter(new(Guid.NewGuid(), "Player", room.Code), false);
            room = await Play(host, await host.Snapshot(room.Id), "start", id: "two-truths-and-a-lie");
            var pv = await player.Snapshot(room.Id);
            using var malformed = await player.Post($"/api/rooms/{room.Id}/game", new { commandId = Guid.NewGuid(), action = "submit", sessionId = pv.Game!.SessionId, roundId = pv.Game.RoundId, stageRevision = pv.Game.StageRevision, submission = new { statements = new[] { "one", "two", "three" } } });
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
            submission = Command(pv, "submit", submission: new(["Secret collection first", "Secret collection second", "Secret collection third"], 1));
            using var response = await player.Post($"/api/rooms/{room.Id}/game", submission); response.EnsureSuccessStatusCode();
            using var duplicate = await player.Post($"/api/rooms/{room.Id}/game", submission); Assert.True((await duplicate.Content.ReadFromJsonAsync<CommandResult>())!.Replayed);
            var raw = await host.Client.GetStringAsync($"/api/rooms/{room.Id}");
            Assert.DoesNotContain("Secret collection", raw); Assert.Null((await host.Snapshot(room.Id)).Game!.OwnSubmission);
            await using var hub = host.Hub(factory); var channel = Channel.CreateUnbounded<RoomSnapshot>();
            hub.On<RoomSnapshot>("Snapshot", s => channel.Writer.TryWrite(s)); await hub.StartAsync(); await hub.InvokeAsync("Subscribe", room.Id);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var hidden = await channel.Reader.ReadAsync(timeout.Token); Assert.Null(hidden.Game!.OwnSubmission); Assert.Null(hidden.Game.Presenter);
            room = await host.Snapshot(room.Id); hc = host.Cookies; pc = player.Cookies;
        }
        using (var factory = new ApiFactory(directory))
        using (var host = await Browser.Create(factory, hc))
        using (var player = await Browser.Create(factory, pc))
        {
            room = await host.Snapshot(room.Id); Assert.Equal("collection", room.Game!.Stage);
            Assert.Equal(1, (await player.Snapshot(room.Id)).Game!.OwnSubmission!.LieIndex);
            using var retry = await player.Post($"/api/rooms/{room.Id}/game", submission); Assert.True((await retry.Content.ReadFromJsonAsync<CommandResult>())!.Replayed);
            room = await Play(host, room, "close-collection");
            Assert.NotNull(room.Game!.Presenter); Assert.Equal("Secret collection first", room.Game.Presenter.Statements[0]);
            Assert.Null(room.Game.OwnSubmission); Assert.Empty(room.Game.Results);
            await Denied(player, room, Command(await player.Snapshot(room.Id), "answer", "2"), HttpStatusCode.Forbidden);
            room = await Play(host, room, "answer", "2");
        }
        using (var factory = new ApiFactory(directory))
        using (var host = await Browser.Create(factory, hc))
        {
            room = await host.Snapshot(room.Id); Assert.Equal("2", room.Game!.OwnChoice); Assert.Empty(room.Game.Results);
            room = await Play(host, room, "reveal"); Assert.Equal(1, room.Game!.Results[0].LieIndex);
            Assert.Equal(1, room.Game.Results[0].Totals![1].Count); Assert.Equal("2", Assert.Single(room.Game.Results[0].Choices).Choice);
        }
        using (var factory = new ApiFactory(directory))
        using (var host = await Browser.Create(factory, hc))
        {
            room = await host.Snapshot(room.Id); Assert.Equal("revealed", room.Game!.Stage); Assert.Equal(1, room.Game.Results[0].LieIndex);
            room = await Play(host, room, "finish"); Assert.Null(room.Game!.OwnChoice); room = await Play(host, room, "lobby"); Assert.Null(room.Game);
        }
    }

    [Theory]
    [InlineData("most-likely-to")]
    [InlineData("two-truths-and-a-lie")]
    public async Task RemovalCannotLeakNominationsOrAnUnrevealedPresenterLie(string id)
    {
        using var factory = new ApiFactory(directory);
        using var host = await Browser.Create(factory);
        using var player = await Browser.Create(factory);
        using var other = await Browser.Create(factory);
        var room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
        var joined = (await player.Enter(new(Guid.NewGuid(), "Player", room.Code), false)).Snapshot;
        var third = (await other.Enter(new(Guid.NewGuid(), "Other", room.Code), false)).Snapshot;
        room = await Play(host, await host.Snapshot(room.Id), "start", id: id);
        if (id == "most-likely-to")
        {
            room = await Play(host, room, "answer", joined.YouId.ToString());
            await Play(other, await other.Snapshot(room.Id), "answer", joined.YouId.ToString());
        }
        else
        {
            await Play(player, await player.Snapshot(room.Id), "submit", submission: new(["First private statement", "Second private statement", "Third private statement"], 2));
            room = await Play(host, await host.Snapshot(room.Id), "close-collection");
            room = await Play(host, room, "answer", "3");
        }
        room = (await host.Command(room.Id, new(Guid.NewGuid(), (await host.Snapshot(room.Id)).Version, "remove", joined.YouId))).Snapshot;
        await Denied(player, room, Command(room, "answer", "pass"), HttpStatusCode.Forbidden);
        Assert.Null(room.Game!.OwnChoice);
        if (id == "most-likely-to")
        {
            Assert.Equal(2, room.Game.ResponseCount);
            Assert.All(room.Game.Responses!, response => Assert.True(response.HasResponded));
            room = await Play(host, room, "reveal");
            Assert.Equal(0, room.Game!.Results[0].Totals!.Sum(t => t.Count));
            Assert.Empty(room.Game.Results[0].Choices);
            room = (await host.Command(room.Id, new(Guid.NewGuid(), room.Version, "remove", third.YouId))).Snapshot;
            Assert.Contains(room.Game!.Results[0].Totals!, t => t.Label == "Removed player" && t.Count == 0);
        }
        else
        {
            Assert.Null(room.Game.Presenter); Assert.Empty(room.Game.Options!);
            await Denied(host, room, Command(room, "reveal"), HttpStatusCode.Conflict);
            room = await Play(host, room, "skip");
            Assert.True(room.Game!.Results[0].Skipped);
            Assert.Null(room.Game.Results[0].LieIndex); Assert.Null(room.Game.Results[0].Totals);
            Assert.Equal("Removed player", room.Game.Results[0].PresenterName);
        }
    }
    [Theory]
    [InlineData("would-you-rather")]
    [InlineData("never-have-i-ever")]
    [InlineData("two-truths-and-a-lie")]
    public async Task NamedRevealAndSummaryArePrivateUntilRevealAndDurableAcrossRestarts(string id)
    {
        CookieContainer hc, pc; RoomSnapshot room; Guid playerId;
        using (var factory = new ApiFactory(directory))
        using (var host = await Browser.Create(factory))
        using (var player = await Browser.Create(factory))
        using (var passer = await Browser.Create(factory))
        using (var missing = await Browser.Create(factory))
        using (var outsider = await Browser.Create(factory))
        {
            room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
            playerId = (await player.Enter(new(Guid.NewGuid(), "Player", room.Code), false)).Snapshot.YouId;
            await passer.Enter(new(Guid.NewGuid(), "Passer", room.Code), false);
            await missing.Enter(new(Guid.NewGuid(), "Missing", room.Code), false);
            room = await Play(host, await host.Snapshot(room.Id), "start", id: id);
            if (id == "two-truths-and-a-lie")
            {
                room = await Play(host, room, "submit", submission: new(["First statement", "Second statement", "Third statement"], 1));
                room = await Play(host, room, "close-collection");
            }
            await Play(player, await player.Snapshot(room.Id), "answer", id == "two-truths-and-a-lie" ? "2" : "b");
            await Play(passer, await passer.Snapshot(room.Id), "answer", "pass");
            var stale = Command(await player.Snapshot(room.Id), "answer", "pass");
            await using var revealHub = player.Hub(factory);
            var reveals = Channel.CreateUnbounded<RoomSnapshot>();
            revealHub.On<RoomSnapshot>("Snapshot", s => reveals.Writer.TryWrite(s));
            await revealHub.StartAsync(); await revealHub.InvokeAsync("Subscribe", room.Id);
            using var revealTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var hidden = await reveals.Reader.ReadAsync(revealTimeout.Token);
            Assert.Empty(hidden.Game!.Results);
            Assert.Empty((await host.Snapshot(room.Id)).Game!.Results);
            room = await Play(host, await host.Snapshot(room.Id), "reveal");
            RoomSnapshot revealed;
            do { revealed = await reveals.Reader.ReadAsync(revealTimeout.Token); } while (revealed.Game?.Stage != "revealed");
            Assert.Equal(id == "two-truths-and-a-lie" ? "2" : "b", revealed.Game.Results[0].Choices.Single(c => c.Name == "Player").Choice);
            Assert.Equal("pass", room.Game!.Results[0].Choices.Single(c => c.Name == "Passer").Choice);
            Assert.Equal("missing", (await player.Snapshot(room.Id)).Game!.Results[0].Choices.Single(c => c.Name == "Missing").Choice);
            await Denied(player, room, stale, HttpStatusCode.Conflict);
            Assert.Equal(HttpStatusCode.Forbidden, (await outsider.Client.GetAsync($"/api/rooms/{room.Id}")).StatusCode);
            hc = host.Cookies; pc = player.Cookies;
        }
        using (var factory = new ApiFactory(directory))
        using (var host = await Browser.Create(factory, hc))
        using (var player = await Browser.Create(factory, pc))
        {
            room = await host.Snapshot(room.Id); Assert.NotEmpty(room.Game!.Results[0].Choices);
            await using var hub = player.Hub(factory);
            var channel = Channel.CreateUnbounded<RoomSnapshot>();
            hub.On<RoomSnapshot>("Snapshot", s => channel.Writer.TryWrite(s));
            await hub.StartAsync(); await hub.InvokeAsync("Subscribe", room.Id);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var before = await channel.Reader.ReadAsync(timeout.Token);
            Assert.Equal(id == "two-truths-and-a-lie" ? "2" : "b", before.Game!.Results[0].Choices.Single(c => c.Name == "Player").Choice);
            var finish = Command(room, "finish");
            using var applied = await host.Post($"/api/rooms/{room.Id}/game", finish); applied.EnsureSuccessStatusCode();
            using var replay = await host.Post($"/api/rooms/{room.Id}/game", finish);
            Assert.True((await replay.Content.ReadFromJsonAsync<CommandResult>())!.Replayed);
            RoomSnapshot finished;
            do { finished = await channel.Reader.ReadAsync(timeout.Token); } while (finished.Game?.Stage != "finished");
            var choices = finished.Game.Results[0].Choices;
            Assert.Equal(id == "two-truths-and-a-lie" ? "2" : "b", choices.Single(c => c.Name == "Player").Choice);
            Assert.Equal("pass", choices.Single(c => c.Name == "Passer").Choice);
            Assert.Equal("missing", choices.Single(c => c.Name == "Missing").Choice);
            Assert.Equal(id == "two-truths-and-a-lie" ? 3 : 4, choices.Count);
            room = (await host.Command(room.Id, new(Guid.NewGuid(), (await host.Snapshot(room.Id)).Version, "remove", playerId))).Snapshot;
        }
        using (var factory = new ApiFactory(directory))
        using (var host = await Browser.Create(factory, hc))
        using (var player = await Browser.Create(factory, pc))
        {
            var restored = await host.Snapshot(room.Id);
            Assert.Equal("finished", restored.Game!.Stage);
            Assert.Equal("Removed player", restored.Game.Results[0].Choices.Single(c => c.ParticipantId == playerId).Name);
            Assert.Equal(JsonSerializer.Serialize(room.Game), JsonSerializer.Serialize(restored.Game));
            Assert.Equal(HttpStatusCode.Forbidden, (await player.Client.GetAsync($"/api/rooms/{room.Id}")).StatusCode);
        }
    }

    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
