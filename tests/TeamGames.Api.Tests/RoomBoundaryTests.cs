using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using TeamGames.Api.Rooms;
using TeamGames.Infrastructure.Rooms;

namespace TeamGames.Api.Tests;

public sealed class RoomBoundaryTests
{
    [Theory]
    [InlineData("http://localhost:8080", false, true)]
    [InlineData("https://attacker.invalid", false, false)]
    [InlineData("null", false, false)]
    [InlineData("", true, false)]
    [InlineData("http://localhost:8080", true, true)]
    public void BrowserOriginsAreCheckedIncludingWebSocketUpgrades(string origin, bool websocket, bool allowed)
    {
        var request = new DefaultHttpContext().Request;
        request.Scheme = "http";
        request.Host = new("localhost:8080");
        request.Headers.Origin = origin;
        if (websocket) request.Headers.Upgrade = "websocket";
        Assert.Equal(allowed, RoomEndpoints.OriginAllowed(request, new ConfigurationBuilder().Build()));
    }

    [Fact]
    public void ConfiguredHttpsOriginIsUsedBehindTheProxy()
    {
        var request = new DefaultHttpContext().Request;
        request.Scheme = "http";
        request.Host = new("api:8080");
        request.Headers.Origin = "https://mingle.example";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Security:PublicOrigin"] = "https://mingle.example" }).Build();
        Assert.True(RoomEndpoints.OriginAllowed(request, configuration));
        request.Headers["Sec-Fetch-Site"] = "cross-site";
        Assert.False(RoomEndpoints.OriginAllowed(request, configuration));
    }

    [Fact]
    public void ExplicitDefaultHttpsPortHasTheSameOriginAsTheBrowserCanonicalForm()
    {
        var request = new DefaultHttpContext().Request; request.Headers.Origin = "https://mingle.example";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Security:PublicOrigin"] = "https://mingle.example:443/" }).Build();
        Assert.True(RoomEndpoints.OriginAllowed(request, configuration));
        request.Headers.Origin = "https://mingle.example:8443";
        Assert.False(RoomEndpoints.OriginAllowed(request, configuration));
    }
    [Fact]
    public void PresenceCountsPeopleAcrossTabsAndAStaleDisconnectCannotRemoveNewConnections()
    {
        var time = new ManualClock();
        var presence = new PresenceRegistry(time);
        var room = Guid.NewGuid();
        var guest = Guid.NewGuid();
        var participant = Guid.NewGuid();
        presence.Subscribe(new("old-tab", room, guest, participant, () => { }));
        presence.Subscribe(new("new-tab", room, guest, participant, () => { }));
        Assert.Single(presence.Read(room).Online);
        presence.Disconnect("old-tab");
        presence.Disconnect("old-tab");
        time.Advance(TimeSpan.FromSeconds(6));
        presence.Sweep();
        Assert.Contains(participant, presence.Read(room).Online);
        presence.Disconnect("new-tab");
        Assert.Contains(participant, presence.Read(room).Online);
        var version = presence.Read(room).Revision;
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Contains(room, presence.Sweep());
        Assert.Empty(presence.Read(room).Online);
        Assert.True(presence.Read(room).Revision > version);
    }
}
