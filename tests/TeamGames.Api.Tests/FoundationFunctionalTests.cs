using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamGames.Core.Games;
using TeamGames.Infrastructure.Persistence;

namespace TeamGames.Api.Tests;

public sealed class FoundationFunctionalTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "teamgames-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CatalogueAndEveryDetailAreAvailableThroughTheRealApi()
    {
        await using var factory = new ApiFactory(directory);
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/games");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        var games = Assert.IsType<GameSummary[]>(await response.Content.ReadFromJsonAsync<GameSummary[]>());
        Assert.Equal(5, games.Length);
        foreach (var game in games)
        {
            Assert.Equal(game, await client.GetFromJsonAsync<GameSummary>($"/api/games/{game.Id}"));
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/games/unknown")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/unknown")).StatusCode);
    }

    [Fact]
    public async Task StartupAppliesMigrationsAndReadinessDetectsLostStorage()
    {
        await using var factory = new ApiFactory(directory);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health/ready")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<TeamGamesDbContext>();
            Assert.Single(await database.Database.GetAppliedMigrationsAsync());
            await database.Database.ExecuteSqlRawAsync("DROP TABLE Installations;");
        }
        var failed = await client.GetAsync("/api/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal("{\"status\":\"unavailable\"}", await failed.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health/live")).StatusCode);
    }

    [Fact]
    public async Task DatabaseAndProtectedValuesSurviveACompleteHostRestart()
    {
        Guid instanceId;
        string protectedValue;
        await using (var first = new ApiFactory(directory))
        {
            using var client = first.CreateClient();
            (await client.GetAsync("/api/health/ready")).EnsureSuccessStatusCode();
            using var scope = first.Services.CreateScope();
            instanceId = (await scope.ServiceProvider.GetRequiredService<TeamGamesDbContext>().Installations.SingleAsync()).InstanceId;
            protectedValue = first.Services.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("restart-test").Protect("persisted guest identity");
        }

        await using var restarted = new ApiFactory(directory);
        using var restartedClient = restarted.CreateClient();
        (await restartedClient.GetAsync("/api/health/ready")).EnsureSuccessStatusCode();
        using var restartedScope = restarted.Services.CreateScope();
        Assert.Equal(instanceId, (await restartedScope.ServiceProvider.GetRequiredService<TeamGamesDbContext>()
            .Installations.SingleAsync()).InstanceId);
        Assert.Equal("persisted guest identity", restarted.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("restart-test").Unprotect(protectedValue));
    }

    [Theory]
    [InlineData("Development", HttpStatusCode.OK)]
    [InlineData("Production", HttpStatusCode.NotFound)]
    public async Task OpenApiIsOnlyExposedInDevelopment(string environment, HttpStatusCode expected)
    {
        await using var factory = new ApiFactory(directory, environment);
        using var client = factory.CreateClient();
        Assert.Equal(expected, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }

    [Fact]
    public void InvalidStorageConfigurationStopsStartup()
    {
        using var factory = new ApiFactory("");
        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("Storage:DataDirectory", exception.ToString());
    }

    [Fact]
    public void UnwritableStorageStopsStartup()
    {
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(filePath, "occupied");
        using var factory = new ApiFactory(filePath);
        Assert.ThrowsAny<IOException>(() => factory.CreateClient());
        Assert.Equal("occupied", File.ReadAllText(filePath));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
