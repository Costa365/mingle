using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR.Client;
using TeamGames.Core.Administration;
using TeamGames.Core.Rooms;
using TeamGames.Core.Games;
using TeamGames.Core.Prompts;
using TeamGames.Infrastructure.Persistence;
using TeamGames.Infrastructure.Rooms;
using Browser = TeamGames.Api.Tests.RoomFunctionalTests.Browser;

namespace TeamGames.Api.Tests;

[Trait("Layer", "Functional")]
public sealed class AdminFunctionalTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "mingle-admin-" + Guid.NewGuid().ToString("N"));
    private const string Credential = "test-only-administrator-token-not-a-real-secret";
    private static Dictionary<string, string?> Settings => new() { ["Admin:Credential"] = Credential };
    private static async Task<HttpResponseMessage> Post(Browser browser, string path, object payload)
    {
        var session = (await browser.Client.GetFromJsonAsync<AdminSessionView>("/api/admin/session"))!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin" + path) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-CSRF-TOKEN", session.RequestToken);
        return await browser.Client.SendAsync(request);
    }
    private static async Task Login(Browser admin)
    {
        using var response = await Post(admin, "/login", new AdminLogin(Credential)); response.EnsureSuccessStatusCode();
        Assert.True((await response.Content.ReadFromJsonAsync<AdminSessionView>())!.Authenticated);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("mingle.admin=") && c.Contains("httponly", StringComparison.OrdinalIgnoreCase) && c.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase) && c.Contains("path=/api/admin"));
    }
    private static async Task<AdminRoomView> Detail(Browser admin, Guid id) => (await admin.Client.GetFromJsonAsync<AdminRoomView>($"/api/admin/rooms/{id}"))!;
    private static async Task<AdminCommandResult> Command(Browser admin, Guid id, AdminRoomCommand command)
    {
        using var response = await Post(admin, $"/rooms/{id}/commands", command); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AdminCommandResult>())!;
    }
    private static async Task Prompt(Browser admin, AdminPromptCommand command)
    {
        using var response = await Post(admin, "/prompts", command); response.EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task DisabledConfigurationAndGuestCookiesNeverGrantAdminAccess()
    {
        using var factory = new ApiFactory(directory); using var guest = await Browser.Create(factory);
        Assert.False((await guest.Client.GetFromJsonAsync<AdminSessionView>("/api/admin/session"))!.Enabled);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(guest, "/login", new AdminLogin(Credential))).StatusCode);
        foreach (var path in new[] { "/rooms", "/prompts", "/audit", "/rooms/" + Guid.NewGuid() })
            Assert.Equal(HttpStatusCode.Unauthorized, (await guest.Client.GetAsync("/api/admin" + path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(guest, "/prompts", new AdminPromptCommand(Guid.NewGuid(), "delete"))).StatusCode);
    }
    [Fact]
    public async Task LoginForgeryRateLimitExpiryLogoutRestartAndCredentialRotationAreEnforced()
    {
        var clock = new ManualClock(); CookieContainer cookies;
        using (var factory = new ApiFactory(directory, clock: clock, settings: Settings))
        using (var admin = await Browser.Create(factory))
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.PostAsJsonAsync("/api/admin/login", new AdminLogin(Credential))).StatusCode);
            admin.Client.DefaultRequestHeaders.Add("Origin", "https://elsewhere.invalid");
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(admin, "/login", new AdminLogin(Credential))).StatusCode);
            admin.Client.DefaultRequestHeaders.Remove("Origin");
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post(admin, "/login", new AdminLogin("wrong"))).StatusCode);
            await Login(admin); cookies = admin.Cookies;
            Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync("/api/admin/rooms")).StatusCode);
            // An administrator session alone cannot enter another participant's room or hub.
            using var host = await Browser.Create(factory); var room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.Client.GetAsync($"/api/rooms/{room.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.PostAsJsonAsync("/api/admin/logout", new { })).StatusCode);
        }
        using (var factory = new ApiFactory(directory, clock: clock, settings: Settings))
        using (var admin = await Browser.Create(factory, cookies))
        {
            Assert.True((await admin.Client.GetFromJsonAsync<AdminSessionView>("/api/admin/session"))!.Authenticated);
            clock.Advance(TimeSpan.FromMinutes(61));
            Assert.Equal(HttpStatusCode.Unauthorized, (await admin.Client.GetAsync("/api/admin/audit")).StatusCode);
            await Login(admin);
            var copied = new CookieContainer();
            foreach (Cookie cookie in admin.Cookies.GetCookies(new Uri("http://localhost/api/admin")))
                copied.Add(new Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain));
            using var beforeLogout = await Browser.Create(factory, copied);
            Assert.True((await beforeLogout.Client.GetFromJsonAsync<AdminSessionView>("/api/admin/session"))!.Authenticated);
            Assert.Equal(HttpStatusCode.NoContent, (await Post(admin, "/logout", new { })).StatusCode);
            using var duplicate = await Browser.Create(factory, copied);
            Assert.Equal(HttpStatusCode.Unauthorized, (await duplicate.Client.GetAsync("/api/admin/rooms")).StatusCode);
            await Login(admin); cookies = admin.Cookies;
        }
        var rotated = Settings; rotated["Admin:Credential"] = "a-different-deployment-administrator-token";
        using (var factory = new ApiFactory(directory, clock: clock, settings: rotated))
        using (var admin = await Browser.Create(factory, cookies)) Assert.Equal(HttpStatusCode.Unauthorized, (await admin.Client.GetAsync("/api/admin/rooms")).StatusCode);
        var limited = Settings; limited["Admin:LoginRateLimit"] = "2";
        using (var factory = new ApiFactory(directory, settings: limited))
        using (var admin = await Browser.Create(factory))
        {
            await Post(admin, "/login", new AdminLogin("wrong")); await Post(admin, "/login", new AdminLogin("wrong"));
            Assert.Equal(HttpStatusCode.TooManyRequests, (await Post(admin, "/login", new AdminLogin(Credential))).StatusCode);
        }
    }
    [Theory]
    [InlineData("this-or-that")]
    [InlineData("would-you-rather")]
    [InlineData("never-have-i-ever")]
    [InlineData("most-likely-to")]
    [InlineData("two-truths-and-a-lie")]
    public async Task RoomControlsPublishStatusOnlyAndEndEveryGameWithoutImpersonation(string gameId)
    {
        using var factory = new ApiFactory(directory, settings: Settings);
        using var admin = await Browser.Create(factory); using var host = await Browser.Create(factory); using var player = await Browser.Create(factory);
        await Login(admin); var room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
        room = (await player.Enter(new(Guid.NewGuid(), "Player", room.Code), false)).Snapshot;
        using (var response = await host.Post($"/api/rooms/{room.Id}/game", new GameCommand(Guid.NewGuid(), "start", GameId: gameId, ExpectedVersion: room.Version)))
        { response.EnsureSuccessStatusCode(); room = (await response.Content.ReadFromJsonAsync<CommandResult>())!.Snapshot; }
        if (gameId == "two-truths-and-a-lie")
        {
            var g = room.Game!;
            using var submit = await player.Post($"/api/rooms/{room.Id}/game", new GameCommand(Guid.NewGuid(), "submit", g.SessionId, g.RoundId, g.StageRevision, Submission: new(["PRIVATE-ONE", "PRIVATE-TWO", "PRIVATE-THREE"], 2)));
            submit.EnsureSuccessStatusCode();
        }
        var view = await Detail(admin, room.Id);
        var raw = JsonSerializer.Serialize(view, JsonSerializerOptions.Web);
        Assert.DoesNotContain("PRIVATE", raw); Assert.DoesNotContain("lieIndex", raw); Assert.DoesNotContain("ownChoice", raw); Assert.DoesNotContain("guestId", raw); Assert.DoesNotContain("state", raw);
        Assert.Equal(gameId, Assert.Single(view.Games).GameId);
        var updated = new TaskCompletionSource<RoomSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var hub = host.Hub(factory); hub.On<RoomSnapshot>("Snapshot", s => { if (s.JoinLocked) updated.TrySetResult(s); });
        await hub.StartAsync(); await hub.InvokeAsync("Subscribe", room.Id);
        var command = new AdminRoomCommand(Guid.NewGuid(), view.Version, "lock", Locked: true);
        var applied = await Command(admin, room.Id, command);
        Assert.True((await updated.Task.WaitAsync(TimeSpan.FromSeconds(10))).JoinLocked);
        Assert.True((await Command(admin, room.Id, command)).Replayed);
        Assert.Equal(applied.AppliedVersion, (await Detail(admin, room.Id)).Version);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, $"/rooms/{room.Id}/commands", command with { Locked = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, $"/rooms/{room.Id}/commands", command with { CommandId = Guid.NewGuid() })).StatusCode);
        view = await Detail(admin, room.Id);
        await Command(admin, room.Id, new(Guid.NewGuid(), view.Version, "lock", Locked: false));
        view = await Detail(admin, room.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, $"/rooms/{room.Id}/commands", new AdminRoomCommand(Guid.NewGuid(), view.Version, "remove", room.FacilitatorId))).StatusCode);
        await Command(admin, room.Id, new(Guid.NewGuid(), view.Version, "end-game"));
        Assert.Equal("finished", (await host.Snapshot(room.Id)).Game!.Stage);
        view = await Detail(admin, room.Id); var playerId = view.Participants.Single(p => p.Name == "Player").Id;
        await Command(admin, room.Id, new(Guid.NewGuid(), view.Version, "transfer", playerId));
        view = await Detail(admin, room.Id); Assert.Equal(playerId, view.FacilitatorId);
        await Command(admin, room.Id, new(Guid.NewGuid(), view.Version, "remove", room.YouId));
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync($"/api/rooms/{room.Id}")).StatusCode);
        view = await Detail(admin, room.Id); await Command(admin, room.Id, new(Guid.NewGuid(), view.Version, "close"));
        view = await Detail(admin, room.Id); Assert.Equal("closed", view.Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, $"/rooms/{room.Id}/commands", new AdminRoomCommand(Guid.NewGuid(), view.Version, "delete"))).StatusCode);
        var deletion = new AdminRoomCommand(Guid.NewGuid(), view.Version, "delete", Confirmation: view.Code);
        await Command(admin, room.Id, deletion); Assert.True((await Command(admin, room.Id, deletion)).Replayed);
        Assert.Equal(HttpStatusCode.NotFound, (await player.Client.GetAsync($"/api/rooms/{room.Id}")).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TeamGamesDbContext>();
        Assert.Empty(await db.Memberships.Where(m => m.RoomId == room.Id).ToArrayAsync()); Assert.Empty(await db.Receipts.Where(r => r.RoomId == room.Id).ToArrayAsync());
        Assert.Contains(await db.AdminAudits.ToArrayAsync(), a => a.Action == "room-delete");
    }
    [Fact]
    public async Task SharedPromptCrudDeletionTombstonesAuditAndRetentionSurviveRestart()
    {
        var clock = new ManualClock(); CookieContainer cookies; string id; string deletedSeed;
        using (var factory = new ApiFactory(directory, clock: clock, settings: Settings))
        using (var admin = await Browser.Create(factory))
        using (var host = await Browser.Create(factory))
        {
            await Login(admin); cookies = admin.Cookies;
            var page = (await admin.Client.GetFromJsonAsync<AdminPromptPage>("/api/admin/prompts?pageSize=1"))!;
            Assert.Equal(350, page.Total); Assert.Single(page.Prompts); deletedSeed = page.Prompts[0].Id;
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.GetAsync("/api/admin/rooms?page=0")).StatusCode);
            var input = new PromptInput("this-or-that", "Admin category", ["custom"], "Shared special choice", "PRIVATEPROMPT-A", "PRIVATEPROMPT-B");
            var create = new AdminPromptCommand(Guid.NewGuid(), "create", input);
            await Prompt(admin, create); await Prompt(admin, create);
            var custom = Assert.Single((await admin.Client.GetFromJsonAsync<AdminPromptPage>("/api/admin/prompts?search=Shared%20special"))!.Prompts); id = custom.Id;
            Assert.Equal("administrator", custom.Source);
            Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, "/prompts", create with { CommandId = Guid.NewGuid() })).StatusCode);
            var room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
            using (var started = await host.Post($"/api/rooms/{room.Id}/game", new GameCommand(Guid.NewGuid(), "start", GameId: "this-or-that", ExpectedVersion: room.Version, PromptId: id))) started.EnsureSuccessStatusCode();
            await Prompt(admin, new(Guid.NewGuid(), "edit", input with { Text = "Changed shared choice", OptionA = "Edited option" }, id, custom.Revision));
            var refreshed = await host.Snapshot(room.Id);
            Assert.True(refreshed.Version > room.Version + 1);
            Assert.Equal("PRIVATEPROMPT-A", refreshed.Game!.OptionA);
            Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, "/prompts", new AdminPromptCommand(Guid.NewGuid(), "disable", PromptId: id, PromptRevision: custom.Revision))).StatusCode);
            await Prompt(admin, new(Guid.NewGuid(), "disable", PromptId: id, PromptRevision: 2));
            Assert.DoesNotContain((await host.Client.GetFromJsonAsync<PromptPage>($"/api/rooms/{room.Id}/prompts"))!.Prompts, p => p.Id == id);
            await Prompt(admin, new(Guid.NewGuid(), "enable", PromptId: id, PromptRevision: 3));
            await Prompt(admin, new(Guid.NewGuid(), "delete", PromptId: id, PromptRevision: 4));
            await Prompt(admin, new(Guid.NewGuid(), "delete", PromptId: deletedSeed, PromptRevision: page.Prompts[0].Revision));
            var raw = await admin.Client.GetStringAsync("/api/admin/audit?pageSize=100");
            Assert.DoesNotContain(Credential, raw); Assert.DoesNotContain("PRIVATEPROMPT", raw); Assert.DoesNotContain("Shared special", raw); Assert.DoesNotContain("fingerprint", raw);
        }
        using (var factory = new ApiFactory(directory, clock: clock, settings: Settings))
        using (var admin = await Browser.Create(factory, cookies))
        {
            Assert.Equal(349, (await admin.Client.GetFromJsonAsync<AdminPromptPage>("/api/admin/prompts"))!.Total);
            using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TeamGamesDbContext>();
            Assert.False(await db.Prompts.AnyAsync(p => p.Id == id || p.Id == deletedSeed));
            Assert.True(await db.PromptTombstones.AnyAsync(p => p.Id == deletedSeed));
            clock.Advance(TimeSpan.FromDays(8)); await scope.ServiceProvider.GetRequiredService<RoomStore>().SweepAsync();
            Assert.Empty(await db.AdminAudits.ToArrayAsync()); Assert.Empty(await db.AdminSessions.ToArrayAsync());
        }
    }
    [Fact]
    public async Task ExistingM4DatabaseUpgradesWithoutLosingPrivateGameState()
    {
        Directory.CreateDirectory(directory);
        var clock = new ManualClock();
        var room = new Room { Id = Guid.NewGuid(), Code = "ABC234", LastActivityTicks = clock.GetUtcNow().Ticks };
        var host = RoomRules.Join(room, Guid.NewGuid(), "Host", 20); room.FacilitatorId = host.Id;
        var modules = new GameModules([new TwoTruthsAndALie()]);
        modules.Apply(room, host, new(Guid.NewGuid(), "start", GameId: "two-truths-and-a-lie", ExpectedVersion: room.Version), clock.GetUtcNow().Ticks);
        var game = modules.Project(room, host)!;
        modules.Apply(room, host, new(Guid.NewGuid(), "submit", game.SessionId, game.RoundId, game.StageRevision, Submission: new(["Preserved one", "Preserved two", "Preserved three"], 1)), clock.GetUtcNow().Ticks);
        var before = room.GameState;
        await using (var old = new TeamGamesDbContext(new DbContextOptionsBuilder<TeamGamesDbContext>().UseSqlite("Data Source=" + Path.Combine(directory, "teamgames.db")).Options))
        {
            await old.Database.GetService<IMigrator>().MigrateAsync("20261025083127_PromptLibrary");
            old.Rooms.Add(room); await old.SaveChangesAsync();
        }
        using var factory = new ApiFactory(directory, clock: clock, settings: Settings);
        using var admin = await Browser.Create(factory); await Login(admin);
        Assert.Equal("collection", Assert.Single((await Detail(admin, room.Id)).Games).Stage);
        using var scope = factory.Services.CreateScope(); var database = scope.ServiceProvider.GetRequiredService<TeamGamesDbContext>();
        var restored = await database.Rooms.Include(r => r.Members).SingleAsync(r => r.Id == room.Id);
        Assert.Equal(before, restored.GameState);
        Assert.Equal(1, modules.Project(restored, restored.Members.Single())!.OwnSubmission!.LieIndex);
        Assert.Equal(5, (await database.Database.GetAppliedMigrationsAsync()).Count());
    }
    [Fact]
    public async Task ActiveDeletionEndsConnectedAccessAndCascadesPrivateRoomData()
    {
        using var factory = new ApiFactory(directory, settings: Settings);
        using var admin = await Browser.Create(factory); await Login(admin);
        using var host = await Browser.Create(factory);
        var room = (await host.Enter(new(Guid.NewGuid(), "Host"))).Snapshot;
        using (var edit = await host.Post($"/api/rooms/{room.Id}/prompts", new PromptCommand(Guid.NewGuid(), room.Version, "create", new("never-have-i-ever", "Room only", [], "Private room prompt"))))
        { edit.EnsureSuccessStatusCode(); room = (await edit.Content.ReadFromJsonAsync<CommandResult>())!.Snapshot; }
        using (var start = await host.Post($"/api/rooms/{room.Id}/game", new GameCommand(Guid.NewGuid(), "start", GameId: "two-truths-and-a-lie", ExpectedVersion: room.Version)))
        { start.EnsureSuccessStatusCode(); room = (await start.Content.ReadFromJsonAsync<CommandResult>())!.Snapshot; }
        var ended = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var hub = host.Hub(factory);
        hub.On<JsonElement>("AccessEnded", value => ended.TrySetResult(value.GetProperty("code").GetString()!));
        await hub.StartAsync(); await hub.InvokeAsync("Subscribe", room.Id);
        await Command(admin, room.Id, new(Guid.NewGuid(), room.Version, "delete", Confirmation: room.Code));
        Assert.Equal("room_missing", await ended.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/api/rooms/{room.Id}")).StatusCode);
        using var newcomer = await Browser.Create(factory);
        Assert.Equal(HttpStatusCode.NotFound, (await newcomer.Post("/api/rooms/join", new EntryRequest(Guid.NewGuid(), "New", room.Code))).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TeamGamesDbContext>();
        Assert.False(await db.Rooms.AnyAsync(r => r.Id == room.Id));
        Assert.False(await db.Prompts.AnyAsync(p => p.RoomId == room.Id));
        Assert.False(await db.Memberships.AnyAsync(m => m.RoomId == room.Id));
        Assert.False(await db.Receipts.AnyAsync(r => r.RoomId == room.Id));
    }
    [Fact]
    public async Task SecureAdministratorCookieUsesConfiguredAbsoluteNonSlidingLifetime()
    {
        var clock = new ManualClock(); var settings = Settings;
        settings["Security:AllowInsecureCookies"] = "false"; settings["Admin:SessionMinutes"] = "1";
        using var factory = new ApiFactory(directory, clock: clock, settings: settings);
        using var admin = await Browser.Create(factory, https: true);
        await Login(admin);
        var cookie = admin.Cookies.GetCookies(new Uri("https://localhost/api/admin"))["mingle.admin"]!;
        Assert.True(cookie.Secure); Assert.True(cookie.HttpOnly); Assert.Equal("/api/admin", cookie.Path);
        var session = (await admin.Client.GetFromJsonAsync<AdminSessionView>("/api/admin/session"))!;
        Assert.Equal(clock.GetUtcNow().AddMinutes(1).Ticks, session.ExpiresTicks);
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(session.ExpiresTicks, (await admin.Client.GetFromJsonAsync<AdminSessionView>("/api/admin/session"))!.ExpiresTicks);
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.Client.GetAsync("/api/admin/rooms")).StatusCode);
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
