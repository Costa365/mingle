using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TeamGames.Api.Deployment;
using TeamGames.Core.Prompts;
using TeamGames.Infrastructure.Prompts;

namespace TeamGames.Api.Tests;

public sealed class DeploymentTests
{
    [Fact]
    public async Task ProductionHostFilterAllowsLoopbackReadinessAndPublicHostButRejectsOthers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mingle-host-filter-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var factory = new ApiFactory(directory, settings: new()
            {
                ["AllowedHosts"] = "mingle.costa365.site;localhost;127.0.0.1"
            });
            using var client = factory.CreateClient();
            foreach (var host in new[] { "127.0.0.1:8080", "localhost:8080", "mingle.costa365.site" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health/ready");
                request.Headers.Host = host;
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            using var invalid = new HttpRequestMessage(HttpMethod.Get, "/api/health/ready");
            invalid.Headers.Host = "attacker.invalid";
            using var rejected = await client.SendAsync(invalid);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void ProxyConfigurationFailsClosedUnlessOriginAndCookiesAreSecure()
    {
        Assert.Null(DeploymentSettings.Read(new ConfigurationBuilder().Build()).KnownProxy);
        var config = new Dictionary<string, string?> { ["Proxy:KnownProxy"] = "10.1.2.3", ["Security:PublicOrigin"] = "http://example.test" };
        Assert.Throws<InvalidOperationException>(() => DeploymentSettings.Read(new ConfigurationBuilder().AddInMemoryCollection(config).Build()));
        config["Security:PublicOrigin"] = "https://example.test/path";
        Assert.Throws<InvalidOperationException>(() => DeploymentSettings.Read(new ConfigurationBuilder().AddInMemoryCollection(config).Build()));
        config["Security:PublicOrigin"] = "https://example.test";
        Assert.Equal(IPAddress.Parse("10.1.2.3"), DeploymentSettings.Read(new ConfigurationBuilder().AddInMemoryCollection(config).Build()).KnownProxy);
        config["Security:AllowInsecureCookies"] = "true";
        Assert.Throws<InvalidOperationException>(() => DeploymentSettings.Read(new ConfigurationBuilder().AddInMemoryCollection(config).Build()));
    }
    [Theory]
    [InlineData("10.1.2.3", "https", "203.0.113.5")]
    [InlineData("10.1.2.4", "http", "10.1.2.4")]
    public async Task OnlyTheExplicitProxyCanSetSchemeAndClientAddress(string peer, string expectedScheme, string expectedIp)
    {
        var context = new DefaultHttpContext(); context.Request.Scheme = "http"; context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Headers["X-Forwarded-Proto"] = "https"; context.Request.Headers["X-Forwarded-For"] = "203.0.113.5";
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(new DeploymentSettings(IPAddress.Parse("10.1.2.3")).Forwarding()));
        await middleware.Invoke(context);
        Assert.Equal(expectedScheme, context.Request.Scheme); Assert.Equal(expectedIp, context.Connection.RemoteIpAddress!.ToString());
    }
    [Fact]
    public async Task CachedPromptsInvalidateWithoutCachingAuthorizationOrAnOldInflightRead()
    {
        var cache = new PromptCache(); var room = Guid.NewGuid(); var loads = 0;
        Task<Prompt[]> Load() { loads++; return Task.FromResult(new[] { new Prompt { Text = "fresh" } }); }
        Assert.Same(await cache.ReadAsync(room, Load), await cache.ReadAsync(room, Load)); Assert.Equal(1, loads);
        cache.Invalidate(); await cache.ReadAsync(room, Load); Assert.Equal(2, loads);
        cache.Remove(room);
        var pending = new TaskCompletionSource<Prompt[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = true;
        var reading = cache.ReadAsync(room, () => { if (first) { first = false; return pending.Task; } return Load(); });
        cache.Invalidate(); pending.SetResult([new Prompt { Text = "stale" }]);
        Assert.Equal("fresh", Assert.Single(await reading).Text);
    }
    [Fact]
    public async Task CacheCapacityIsBoundedAndNeverMixesRoomLocalContent()
    {
        var cache = new PromptCache(1); var first = Guid.NewGuid(); var second = Guid.NewGuid(); var loads = 0;
        Task<Prompt[]> Load(string text) { loads++; return Task.FromResult(new[] { new Prompt { Text = text } }); }
        Assert.Equal("first room", Assert.Single(await cache.ReadAsync(first, () => Load("first room"))).Text);
        Assert.Equal("second room", Assert.Single(await cache.ReadAsync(second, () => Load("second room"))).Text);
        Assert.Equal("first refreshed", Assert.Single(await cache.ReadAsync(first, () => Load("first refreshed"))).Text);
        Assert.Equal(3, loads);
    }
    [Fact]
    public async Task OversizedRequestsAreRejectedByTheRealApiWithoutReflectingTheirContent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mingle-boundary-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var factory = new ApiFactory(directory); using var client = factory.CreateClient();
            using var response = await client.PostAsJsonAsync("/api/session", new { padding = new string('x', DeploymentSecurity.MaximumBodyBytes + 1) });
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.DoesNotContain(new string('x', 100), await response.Content.ReadAsStringAsync());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
