using Microsoft.Extensions.Diagnostics.HealthChecks;
using TeamGames.Api.Health;
using TeamGames.Core.Storage;

namespace TeamGames.Api.Tests;

public sealed class StorageHealthCheckTests
{
    [Theory]
    [InlineData(true, HealthStatus.Healthy)]
    [InlineData(false, HealthStatus.Unhealthy)]
    public async Task ReadinessRequiresInitializedStorage(bool ready, HealthStatus expected)
    {
        var check = new StorageHealthCheck(new Probe(_ => Task.FromResult(ready)));
        Assert.Equal(expected, (await check.CheckHealthAsync(new())).Status);
    }

    [Fact]
    public async Task StorageFailuresAreUnhealthyWithoutExposingTheirDetails()
    {
        var check = new StorageHealthCheck(new Probe(_ => throw new IOException("private/database/path")));
        var result = await check.CheckHealthAsync(new());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.DoesNotContain("private", result.Description);
        Assert.Null(result.Exception);
    }

    [Fact]
    public async Task CancellationIsForwardedToTheProbe()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var check = new StorageHealthCheck(new Probe(token => Task.FromCanceled<bool>(token)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.CheckHealthAsync(new(), cancellation.Token));
    }

    private sealed class Probe(Func<CancellationToken, Task<bool>> check) : IStorageProbe
    {
        public Task<bool> IsReadyAsync(CancellationToken cancellationToken) => check(cancellationToken);
    }
}
