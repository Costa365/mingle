using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamGames.Core.Rooms;
using TeamGames.Infrastructure.Persistence;
using TeamGames.Infrastructure.Rooms;

namespace TeamGames.Api.Tests;

public sealed class RoomFunctionalTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "mingle-room-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GuestCookiesAndCsrfProtectEveryMutationAndHubOriginsAreChecked()
    {
        await using var factory = new ApiFactory(directory);
        using var guest = await Browser.Create(factory);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/rooms/{Guid.NewGuid()}")).StatusCode);
        var request = new EntryRequest(Guid.NewGuid(), "Host");
        using var noToken = new HttpRequestMessage(HttpMethod.Post, "/api/rooms") { Content = JsonContent.Create(request) };
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.Client.SendAsync(noToken)).StatusCode);
        var room = await guest.Enter(request);
        foreach (var path in new[] { "/api/session", "/api/rooms/join", $"/api/rooms/{room.Snapshot.Id}/commands" })
        {
            using var missing = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = path.EndsWith("commands", StringComparison.Ordinal)
                    ? JsonContent.Create(new RoomCommand(Guid.NewGuid(), room.Snapshot.Version, "close"))
                    : JsonContent.Create(request)
            };
            Assert.Equal(HttpStatusCode.BadRequest, (await guest.Client.SendAsync(missing)).StatusCode);
        }
        using var crossOrigin = new HttpRequestMessage(HttpMethod.Post, "/api/rooms") { Content = JsonContent.Create(request) };
        crossOrigin.Headers.Add("Origin", "https://attacker.invalid");
        crossOrigin.Headers.Add("X-CSRF-TOKEN", guest.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.Client.SendAsync(crossOrigin)).StatusCode);
        using var hubRequest = new HttpRequestMessage(HttpMethod.Post, "/hubs/rooms/negotiate?negotiateVersion=1");
        hubRequest.Headers.Add("Origin", "https://attacker.invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.Client.SendAsync(hubRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await guest.Client.GetAsync($"/api/rooms/{room.Snapshot.Id}")).StatusCode);
        var cookie = guest.Cookies.GetCookies(new Uri("http://localhost"))["mingle.guest"]!;
        Assert.True(cookie.HttpOnly);
        using var forged = factory.CreateDefaultClient(new CookieHandler(new CookieContainer()));
        forged.DefaultRequestHeaders.Add("Cookie", "mingle.guest=forged");
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync($"/api/rooms/{room.Snapshot.Id}")).StatusCode);
    }

    [Fact]
    public async Task CreationJoinValidationCapacityAndRetryReceiptsUseRealStorage()
    {
        await using var factory = new ApiFactory(directory, settings: new() { ["Rooms:Capacity"] = "2" });
        using var host = await Browser.Create(factory);
        using var player = await Browser.Create(factory);
        using var other = await Browser.Create(factory);
        var create = new EntryRequest(Guid.NewGuid(), " Host ");
        var first = await host.Enter(create);
        var retry = await host.Enter(create);
        Assert.True(retry.Replayed);
        Assert.Equal(first.Snapshot.Id, retry.Snapshot.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Post("/api/rooms", create with { DisplayName = "Changed" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await player.Post("/api/rooms/join", new EntryRequest(Guid.NewGuid(), "", first.Snapshot.Code))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await player.Post("/api/rooms/join", new EntryRequest(Guid.NewGuid(), "Player", "bad"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await player.Post("/api/rooms/join", new EntryRequest(Guid.NewGuid(), "Player", "ZZZZZZ"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await player.Post("/api/rooms/join", new EntryRequest(Guid.NewGuid(), "HOST", first.Snapshot.Code))).StatusCode);
        var join = new EntryRequest(Guid.NewGuid(), "Player", first.Snapshot.Code.ToLowerInvariant());
        var joined = await player.Enter(join, false);
        Assert.True((await player.Enter(join, false)).Replayed);
        Assert.Equal(2, joined.Snapshot.Participants.Count);
        Assert.Equal(HttpStatusCode.Conflict, (await other.Post("/api/rooms/join", new EntryRequest(Guid.NewGuid(), "Other", first.Snapshot.Code))).StatusCode);
        var reentered = await player.Enter(join with { CommandId = Guid.NewGuid(), DisplayName = "Another name" }, false);
        Assert.Equal(joined.Snapshot.YouId, reentered.Snapshot.YouId);
        Assert.Equal(2, reentered.Snapshot.Participants.Count);
        var json = JsonSerializer.Serialize(joined);
        foreach (var secretField in new[] { "GuestId", "Verifier", "Fingerprint", "ExpiresTicks" }) Assert.DoesNotContain(secretField, json);
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TeamGamesDbContext>();
        Assert.Single(await database.Rooms.ToArrayAsync());
        Assert.Equal(2, await database.Memberships.CountAsync());
        Assert.Equal(3, await database.Receipts.CountAsync());
    }

    [Fact]
    public async Task FacilitatorControlsEnforcePermissionsVersionsAndIdempotency()
    {
        await using var factory = new ApiFactory(directory);
        using var host = await Browser.Create(factory);
        using var player = await Browser.Create(factory);
        using var outsider = await Browser.Create(factory);
        var created = await host.Enter(new(Guid.NewGuid(), "Host"));
        var joined = await player.Enter(new(Guid.NewGuid(), "Player", created.Snapshot.Code), false);
        var id = created.Snapshot.Id;
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.Client.GetAsync($"/api/rooms/{id}")).StatusCode);
        var locked = new RoomCommand(Guid.NewGuid(), joined.Snapshot.Version, "lock", Locked: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Post($"/api/rooms/{id}/commands", locked)).StatusCode);
        var commands = await Task.WhenAll(host.Command(id, locked), host.Command(id, locked));
        Assert.Single(commands, c => c.Replayed);
        Assert.Equal(joined.Snapshot.Version + 1, commands[0].Snapshot.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Post($"/api/rooms/{id}/commands", locked with { CommandId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Post($"/api/rooms/{id}/commands", locked with { Locked = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await outsider.Post("/api/rooms/join", new EntryRequest(Guid.NewGuid(), "Outsider", created.Snapshot.Code))).StatusCode);
        Assert.Equal(joined.Snapshot.YouId, (await player.Enter(new(Guid.NewGuid(), "Ignored", created.Snapshot.Code), false)).Snapshot.YouId);
        var transfer = await host.Command(id, new(Guid.NewGuid(), commands[0].Snapshot.Version, "transfer", joined.Snapshot.YouId));
        Assert.Empty(transfer.Snapshot.AllowedActions);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Post($"/api/rooms/{id}/commands", new RoomCommand(Guid.NewGuid(), transfer.Snapshot.Version, "close"))).StatusCode);
        var remove = await player.Command(id, new(Guid.NewGuid(), transfer.Snapshot.Version, "remove", created.Snapshot.YouId));
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync($"/api/rooms/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Post($"/api/rooms/{id}/commands", locked)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Post("/api/rooms/join", new EntryRequest(Guid.NewGuid(), "Host", created.Snapshot.Code))).StatusCode);
        var close = new RoomCommand(Guid.NewGuid(), remove.Snapshot.Version, "close");
        var closed = await player.Command(id, close);
        Assert.Equal("closed", closed.Snapshot.Status);
        Assert.True((await player.Command(id, close)).Replayed);
        await using var closedHub = player.Hub(factory);
        await closedHub.StartAsync();
        await Assert.ThrowsAsync<HubException>(() => closedHub.InvokeAsync("Subscribe", id));
        Assert.Equal(HttpStatusCode.Gone, (await outsider.Post("/api/rooms/join", new EntryRequest(Guid.NewGuid(), "Outsider", created.Snapshot.Code))).StatusCode);
    }

    [Fact]
    public async Task RealHubAuthorizesSubscriptionsCountsTabsAndRevokesRemovedConnections()
    {
        await using var factory = new ApiFactory(directory);
        using var host = await Browser.Create(factory);
        using var player = await Browser.Create(factory);
        using var outsider = await Browser.Create(factory);
        var created = await host.Enter(new(Guid.NewGuid(), "Host"));
        var joined = await player.Enter(new(Guid.NewGuid(), "Player", created.Snapshot.Code), false);
        await using var hostHub = host.Hub(factory);
        await using var playerHub = player.Hub(factory);
        await using var secondTab = player.Hub(factory);
        await using var outsiderHub = outsider.Hub(factory);
        var snapshots = Channel.CreateUnbounded<RoomSnapshot>();
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hostHub.On<RoomSnapshot>("Snapshot", s => snapshots.Writer.TryWrite(s));
        playerHub.On<JsonElement>("AccessEnded", _ => ended.TrySetResult());
        await Task.WhenAll(hostHub.StartAsync(), playerHub.StartAsync(), secondTab.StartAsync(), outsiderHub.StartAsync());
        await Assert.ThrowsAsync<HubException>(() => outsiderHub.InvokeAsync("Subscribe", created.Snapshot.Id));
        await hostHub.InvokeAsync("Subscribe", created.Snapshot.Id);
        await playerHub.InvokeAsync("Subscribe", created.Snapshot.Id);
        await secondTab.InvokeAsync("Subscribe", created.Snapshot.Id);
        var live = await ReadUntil(snapshots.Reader, s => s.Participants.Count(p => p.IsOnline) == 2);
        Assert.Equal(2, live.Participants.Count);
        await secondTab.StopAsync();
        Assert.True((await host.Snapshot(created.Snapshot.Id)).Participants.Single(p => p.Id == joined.Snapshot.YouId).IsOnline);
        await host.Command(created.Snapshot.Id, new(Guid.NewGuid(), live.Version, "remove", joined.Snapshot.YouId));
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var after = await ReadUntil(snapshots.Reader, s => s.Version > live.Version);
        Assert.Single(after.Participants);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Client.GetAsync($"/api/rooms/{created.Snapshot.Id}")).StatusCode);
    }

    [Fact]
    public async Task RestartPreservesCookiesRoomsRolesAndReceipts()
    {
        var cookies = new CookieContainer();
        Guid id;
        Guid participant;
        EntryRequest create = new(Guid.NewGuid(), "Host");
        await using (var factory = new ApiFactory(directory))
        {
            using var first = await Browser.Create(factory, cookies);
            var room = await first.Enter(create);
            id = room.Snapshot.Id;
            participant = room.Snapshot.YouId;
        }
        await using var restarted = new ApiFactory(directory);
        using var recovered = await Browser.Create(restarted, cookies);
        var restored = await recovered.Snapshot(id);
        Assert.Equal(participant, restored.YouId);
        Assert.Equal(participant, restored.FacilitatorId);
        Assert.True((await recovered.Enter(create)).Replayed);
        Assert.Contains("close", restored.AllowedActions);
    }

    [Fact]
    public async Task FakeTimeExpiryRevokesAccessAndRetentionDeletesDependentData()
    {
        var time = new ManualClock();
        await using var factory = new ApiFactory(directory, clock: time);
        using var host = await Browser.Create(factory);
        var room = await host.Enter(new(Guid.NewGuid(), "Host"));
        await using var hub = host.Hub(factory);
        var expired = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<JsonElement>("AccessEnded", value => expired.TrySetResult(value.GetProperty("code").GetString()!));
        await hub.StartAsync();
        await hub.InvokeAsync("Subscribe", room.Snapshot.Id);
        time.Advance(TimeSpan.FromHours(24));
        Assert.Equal(HttpStatusCode.Gone, (await host.Client.GetAsync($"/api/rooms/{room.Snapshot.Id}")).StatusCode);
        await Sweep(factory);
        await factory.Services.GetRequiredService<TeamGames.Api.Rooms.RoomPublisher>().PublishAsync(room.Snapshot.Id);
        Assert.Equal("room_expired", await expired.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        time.Advance(TimeSpan.FromHours(24));
        await Sweep(factory);
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TeamGamesDbContext>();
        Assert.Empty(await database.Rooms.ToArrayAsync());
        Assert.Empty(await database.Memberships.ToArrayAsync());
        Assert.Empty(await database.Receipts.ToArrayAsync());
    }

    [Fact]
    public async Task SecureCookiesAreTheDefaultOnHttps()
    {
        await using var factory = new ApiFactory(directory, settings: new() { ["Security:AllowInsecureCookies"] = "false" });
        using var guest = await Browser.Create(factory, https: true);
        var cookies = guest.Cookies.GetCookies(new Uri("https://localhost"));
        Assert.True(cookies["mingle.guest"]!.Secure);
        Assert.True(cookies["mingle.csrf"]!.Secure);
    }

    [Fact]
    public async Task UpgradingTheFoundationDatabasePreservesItsInstallation()
    {
        Directory.CreateDirectory(directory);
        var instance = Guid.NewGuid();
        await using (var previous = new TeamGamesDbContext(new DbContextOptionsBuilder<TeamGamesDbContext>()
            .UseSqlite($"Data Source={Path.Combine(directory, "teamgames.db")}").Options))
        {
            var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(previous);
            await migrator.MigrateAsync("20261023114150_InitialFoundation");
            previous.Installations.Add(new Installation { Id = 1, InstanceId = instance, CreatedUtc = DateTime.UtcNow });
            await previous.SaveChangesAsync();
        }
        await using var factory = new ApiFactory(directory);
        using var host = await Browser.Create(factory);
        await host.Enter(new(Guid.NewGuid(), "Host"));
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TeamGamesDbContext>();
        Assert.Equal(instance, (await database.Installations.SingleAsync()).InstanceId);
        Assert.Single(await database.Rooms.ToArrayAsync());
    }

    [Fact]
    public async Task GuestExpiryAndEntryRateLimitFailClosed()
    {
        var time = new ManualClock();
        await using var factory = new ApiFactory(directory, clock: time, settings: new() { ["Rooms:GuestDays"] = "1", ["Rooms:EntryRateLimit"] = "3" });
        using var host = await Browser.Create(factory);
        var created = await host.Enter(new(Guid.NewGuid(), "Host"));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.Post("/api/rooms", new EntryRequest(Guid.NewGuid(), "Another"))).StatusCode);
        time.Advance(TimeSpan.FromDays(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync($"/api/rooms/{created.Snapshot.Id}")).StatusCode);
    }

    private static async Task Sweep(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RoomStore>().SweepAsync();
    }

    private static async Task<RoomSnapshot> ReadUntil(ChannelReader<RoomSnapshot> reader, Func<RoomSnapshot, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var snapshot = await reader.ReadAsync(timeout.Token);
            if (predicate(snapshot)) return snapshot;
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private sealed class CookieHandler(CookieContainer cookies) : DelegatingHandler
    {
        public CookieHandler(CookieContainer cookies, HttpMessageHandler inner) : this(cookies) => InnerHandler = inner;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cookie = cookies.GetCookieHeader(request.RequestUri!);
            if (cookie.Length > 0) request.Headers.TryAddWithoutValidation("Cookie", cookie);
            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
                foreach (var value in values) cookies.SetCookies(request.RequestUri!, value);
            return response;
        }
    }

    internal sealed class Browser : IDisposable
    {
        public required HttpClient Client { get; init; }
        public required CookieContainer Cookies { get; init; }
        public string Token { get; private set; } = "";
        private sealed record Session(string RequestToken);
        public static async Task<Browser> Create(ApiFactory factory, CookieContainer? cookies = null, bool https = false)
        {
            cookies ??= new();
            var browser = new Browser { Cookies = cookies, Client = factory.CreateDefaultClient(new Uri(https ? "https://localhost" : "http://localhost"), new CookieHandler(cookies)) };
            browser.Token = (await browser.Client.GetFromJsonAsync<Session>("/api/session"))!.RequestToken;
            var result = await browser.Post("/api/session", new { });
            result.EnsureSuccessStatusCode();
            browser.Token = (await result.Content.ReadFromJsonAsync<Session>())!.RequestToken;
            return browser;
        }
        public async Task<HttpResponseMessage> Post(string path, object payload)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
            request.Headers.Add("X-CSRF-TOKEN", Token);
            return await Client.SendAsync(request);
        }
        public async Task<CommandResult> Enter(EntryRequest request, bool create = true)
        {
            var result = await Post(create ? "/api/rooms" : "/api/rooms/join", request);
            result.EnsureSuccessStatusCode();
            return (await result.Content.ReadFromJsonAsync<CommandResult>())!;
        }
        public async Task<CommandResult> Command(Guid id, RoomCommand command)
        {
            var result = await Post($"/api/rooms/{id}/commands", command);
            result.EnsureSuccessStatusCode();
            return (await result.Content.ReadFromJsonAsync<CommandResult>())!;
        }
        public async Task<RoomSnapshot> Snapshot(Guid id) => (await Client.GetFromJsonAsync<RoomSnapshot>($"/api/rooms/{id}"))!;
        public HubConnection Hub(ApiFactory factory) => new HubConnectionBuilder().WithUrl("http://localhost/hubs/rooms", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.Headers["Origin"] = "http://localhost";
            options.HttpMessageHandlerFactory = _ => new CookieHandler(Cookies, factory.Server.CreateHandler());
        }).Build();
        public void Dispose() => Client.Dispose();
    }
}

internal sealed class ManualClock : TimeProvider
{
    private DateTimeOffset now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan amount) => now += amount;
}
