using Microsoft.Extensions.Diagnostics.HealthChecks;
using TeamGames.Core.Storage;

namespace TeamGames.Api.Health;

public sealed class StorageHealthCheck(IStorageProbe storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await storage.IsReadyAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Storage is not initialized.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Never return database paths or exception details in public probes.
            return HealthCheckResult.Unhealthy("Storage is unavailable.");
        }
    }
}
