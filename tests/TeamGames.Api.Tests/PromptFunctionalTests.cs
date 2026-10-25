using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamGames.Core.Games;
using TeamGames.Core.Prompts;
using TeamGames.Core.Rooms;
using TeamGames.Infrastructure.Persistence;
using TeamGames.Infrastructure.Prompts;
using Browser = TeamGames.Api.Tests.RoomFunctionalTests.Browser;

namespace TeamGames.Api.Tests;

[Trait("Layer", "Functional")]
public sealed class PromptFunctionalTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "mingle-prompts-" + Guid.NewGuid().ToString("N"));
    private static PromptInput Input => new("this-or-that", "Our team", ["custom", "weekend"], "A team choice", "Build a kite", "Build a birdhouse");
    private static async Task<PromptPage> Page(Browser browser, Guid id, string query = "") =>
        (await browser.Client.GetFromJsonAsync<PromptPage>($"/api/rooms/{id}/prompts{query}"))!;
    private static async Task<CommandResult> Edit(Browser browser, RoomSnapshot room, string action, PromptInput? input = null, PromptView? prompt = null)
    {
        using var response = await browser.Post($"/api/rooms/{room.Id}/prompts", new PromptCommand(Guid.NewGuid(), room.Version, action, input, prompt?.Id, prompt?.Revision ?? 0));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CommandResult>())!;
    }
    private static async Task<RoomSnapshot> Play(Browser browser, RoomSnapshot room, string action, string? promptId = null, string? category = null, bool reset = false)
    {
        var game = room.Game;
        using var response = await browser.Post($"/api/rooms/{room.Id}/game", new GameCommand(Guid.NewGuid(), action, game?.SessionId, game?.RoundId,
            game?.StageRevision ?? 0, GameId: "this-or-that", ExpectedVersion: room.Version, PromptId: promptId, Category: category, ResetPool: reset));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CommandResult>())!.Snapshot;
    }
    [Fact]
    public async Task SearchFiltersCountsShapesAndRoomIsolationUseTheRealApi()
    {
        using var factory = new ApiFactory(directory);
        using var host = await Browser.Create(factory);
        using var other = await Browser.Create(factory);
        using var player = await Browser.Create(factory);
        var room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
        var otherRoom = (await other.Enter(new(Guid.NewGuid(), "Other"))).Snapshot;
        Assert.Equal(350, (await Page(host, room.Id)).Prompts.Count);
        Assert.Equal(200, (await Page(host, room.Id, "?gameId=this-or-that")).Prompts.Count);
        Assert.Equal(25, (await Page(host, room.Id, "?gameId=this-or-that&category=Food&tag=preferences")).Prompts.Count);
        Assert.Single((await Page(host, room.Id, "?search=Coffee")).Prompts);
        room = (await Edit(host, room, "create", Input)).Snapshot;
        Assert.Single((await Page(host, room.Id, "?search=birdhouse")).Prompts);
        Assert.Empty((await Page(other, otherRoom.Id, "?search=birdhouse")).Prompts);
        await Edit(other, otherRoom, "create", Input);
        Assert.Single((await Page(other, otherRoom.Id, "?search=birdhouse")).Prompts);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.Client.GetAsync($"/api/rooms/{room.Id}/prompts")).StatusCode);
        await player.Enter(new(Guid.NewGuid(), "Player", room.Code), false);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Client.GetAsync($"/api/rooms/{room.Id}/prompts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Post($"/api/rooms/{room.Id}/prompts", new PromptCommand(Guid.NewGuid(), room.Version, "create", Input))).StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/rooms/{room.Id}/prompts")).StatusCode);
    }
    [Fact]
    public async Task CustomSelectionSnapshotsSurviveEditingDeletingRestartAndExplicitPoolReset()
    {
        CookieContainer cookies;
        RoomSnapshot room;
        string customId;
        using (var factory = new ApiFactory(directory))
        using (var host = await Browser.Create(factory))
        {
            cookies = host.Cookies;
            room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
            room = (await Edit(host, room, "create", Input)).Snapshot;
            var prompt = Assert.Single((await Page(host, room.Id, "?category=Our%20team")).Prompts);
            customId = prompt.Id;
            room = await Play(host, room, "start", prompt.Id, "Our team");
            Assert.Equal("Build a kite", room.Game!.OptionA);
            Assert.True(Assert.Single((await Page(host, room.Id, "?category=Our%20team")).Prompts).Used);
            room = (await Edit(host, room, "edit", Input with { OptionA = "Paint a kite" }, prompt)).Snapshot;
            Assert.Equal("Build a kite", room.Game!.OptionA);
            room = await Play(host, room, "reveal");
            Assert.Contains("reset", room.Game!.AllowedActions);
            room = await Play(host, room, "reset");
            Assert.Equal("Paint a kite", room.Game!.OptionA);
            Assert.Equal("Build a kite", room.Game.Results[0].OptionA);
        }
        using (var factory = new ApiFactory(directory))
        using (var host = await Browser.Create(factory, cookies))
        {
            room = await host.Snapshot(room.Id);
            Assert.Equal(2, room.Game!.RoundNumber);
            Assert.Equal("Paint a kite", room.Game.OptionA);
            var prompt = Assert.Single((await Page(host, room.Id, "?category=Our%20team")).Prompts);
            Assert.Equal(customId, prompt.Id);
            room = (await Edit(host, room, "delete", prompt: prompt)).Snapshot;
            Assert.Empty((await Page(host, room.Id, "?category=Our%20team")).Prompts);
            Assert.Equal("Paint a kite", room.Game!.OptionA);
            room = await Play(host, room, "reveal");
            Assert.Equal(0, room.Game!.PoolSize);
            room = await Play(host, room, "next", category: "Food");
            Assert.Equal(3, room.Game!.RoundNumber);
            Assert.Equal(24, room.Game.RemainingPrompts);
        }
    }
    [Fact]
    public async Task ValidationPermissionsForgeryAndRetryReceiptsProtectEveryPromptMutation()
    {
        using var factory = new ApiFactory(directory);
        using var host = await Browser.Create(factory);
        using var other = await Browser.Create(factory);
        var room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync($"/api/rooms/{room.Id}/prompts?gameId=missing")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync($"/api/rooms/{room.Id}/prompts?search=" + new string('x', 121))).StatusCode);
        var command = new PromptCommand(Guid.NewGuid(), room.Version, "create", Input);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsJsonAsync($"/api/rooms/{room.Id}/prompts", command)).StatusCode);
        host.Client.DefaultRequestHeaders.Add("Origin", "https://elsewhere.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Post($"/api/rooms/{room.Id}/prompts", command)).StatusCode);
        host.Client.DefaultRequestHeaders.Remove("Origin");
        Assert.Equal(HttpStatusCode.Forbidden, (await other.Post($"/api/rooms/{room.Id}/prompts", command)).StatusCode);
        foreach (var input in new[] { Input with { OptionA = "" }, Input with { OptionB = Input.OptionA }, Input with { Tags = ["x", "X"] }, Input with { GameId = "missing" }, Input with { Text = new string('x', 241) } })
            Assert.Equal(HttpStatusCode.BadRequest, (await host.Post($"/api/rooms/{room.Id}/prompts", command with { CommandId = Guid.NewGuid(), Input = input })).StatusCode);
        var responses = await Task.WhenAll(host.Post($"/api/rooms/{room.Id}/prompts", command), host.Post($"/api/rooms/{room.Id}/prompts", command));
        Assert.All(responses, r => r.EnsureSuccessStatusCode());
        Assert.Single((await Page(host, room.Id, "?search=birdhouse")).Prompts);
        Assert.True((await responses[1].Content.ReadFromJsonAsync<CommandResult>())!.Replayed || (await responses[0].Content.ReadFromJsonAsync<CommandResult>())!.Replayed);
        foreach (var response in responses) response.Dispose();
        room = await host.Snapshot(room.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Post($"/api/rooms/{room.Id}/prompts", command with { Input = Input with { OptionA = "Different" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Post($"/api/rooms/{room.Id}/prompts", command with { CommandId = Guid.NewGuid(), ExpectedVersion = room.Version, Input = Input with { OptionA = Input.OptionB, OptionB = Input.OptionA } })).StatusCode);
        var prompt = Assert.Single((await Page(host, room.Id, "?search=birdhouse")).Prompts);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Post($"/api/rooms/{room.Id}/prompts", new PromptCommand(Guid.NewGuid(), room.Version, "delete", PromptId: "tot-001", PromptRevision: 2))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Post($"/api/rooms/{room.Id}/prompts", new PromptCommand(Guid.NewGuid(), room.Version, "delete", PromptId: prompt.Id, PromptRevision: 0))).StatusCode);
        room = (await Edit(host, room, "edit", Input with { Enabled = false }, prompt)).Snapshot;
        Assert.False(Assert.Single((await Page(host, room.Id, "?search=birdhouse")).Prompts).Enabled);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Post($"/api/rooms/{room.Id}/game", new GameCommand(Guid.NewGuid(), "start", GameId: "this-or-that", ExpectedVersion: room.Version, PromptId: prompt.Id))).StatusCode);
    }
    [Fact]
    public async Task SeedRerunsUpdateUntouchedRowsAndPreserveEditedDisabledAndRoomContent()
    {
        using var factory = new ApiFactory(directory);
        using var host = await Browser.Create(factory);
        var room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
        await Edit(host, room, "create", Input);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TeamGamesDbContext>();
        var edited = await database.Prompts.SingleAsync(p => p.Id == "tot-001");
        edited.Text = "Administrator refinement"; edited.Enabled = false;
        await database.SaveChangesAsync();
        var seed = PromptRules.Create(new("this-or-that", "Food", ["preferences"], "A revised seed", "New seed A", "New seed B"), "tot-002", null);
        await PromptSeeder.SeedAsync(database, [PromptLibrary.All[0], seed]);
        Assert.Equal("Administrator refinement", edited.Text);
        Assert.False(edited.Enabled);
        Assert.Equal("A revised seed", (await database.Prompts.SingleAsync(p => p.Id == "tot-002")).Text);
        var originalThird = (await database.Prompts.SingleAsync(p => p.Id == "tot-003")).NormalizedKey;
        var conflicting = PromptRules.Create(new("this-or-that", "Food", [], "Conflicting seed", "New seed A", "New seed B"), "tot-003", null);
        await PromptSeeder.SeedAsync(database, [conflicting]);
        Assert.Equal(originalThird, (await database.Prompts.SingleAsync(p => p.Id == "tot-003")).NormalizedKey);
        var revisions = await database.Prompts.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Revision);
        await PromptSeeder.SeedAsync(database, [PromptLibrary.All[0], seed]);
        Assert.Equal(revisions.OrderBy(p => p.Key), (await database.Prompts.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Revision)).OrderBy(p => p.Key));
        Assert.Single(await database.Prompts.Where(p => p.RoomId == room.Id).ToArrayAsync());
        Assert.Equal(351, await database.Prompts.CountAsync());
    }
    [Fact]
    public async Task FacilitatorTransferClosesOldAccessAndRoomPurgeDeletesOnlyRoomPrompts()
    {
        var clock = new ManualClock();
        using var factory = new ApiFactory(directory, clock: clock);
        using var host = await Browser.Create(factory);
        using var player = await Browser.Create(factory);
        var room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
        var joined = (await player.Enter(new(Guid.NewGuid(), "Player", room.Code), false)).Snapshot;
        room = (await Edit(host, await host.Snapshot(room.Id), "create", Input)).Snapshot;
        room = (await host.Command(room.Id, new(Guid.NewGuid(), room.Version, "transfer", joined.YouId))).Snapshot;
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync($"/api/rooms/{room.Id}/prompts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Post($"/api/rooms/{room.Id}/prompts", new PromptCommand(Guid.NewGuid(), room.Version, "create", Input))).StatusCode);
        var own = Assert.Single((await Page(player, room.Id, "?search=birdhouse")).Prompts);
        Assert.Equal("room", own.Source);
        room = (await player.Command(room.Id, new(Guid.NewGuid(), room.Version, "close"))).Snapshot;
        Assert.Equal(HttpStatusCode.Gone, (await player.Client.GetAsync($"/api/rooms/{room.Id}/prompts")).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await player.Post($"/api/rooms/{room.Id}/prompts", new PromptCommand(Guid.NewGuid(), room.Version, "delete", PromptId: own.Id, PromptRevision: own.Revision))).StatusCode);
        clock.Advance(TimeSpan.FromHours(25));
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TeamGames.Infrastructure.Rooms.RoomStore>().SweepAsync();
        var database = scope.ServiceProvider.GetRequiredService<TeamGamesDbContext>();
        Assert.Equal(350, await database.Prompts.CountAsync());
        Assert.False(await database.Prompts.AnyAsync(p => p.RoomId != null));
    }
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
