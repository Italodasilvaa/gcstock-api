using Girus.Infra.Database;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Girus.Infra.HealthChecks;

public sealed class DatabaseHealthCheck(IDatabaseProbe databaseProbe) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await databaseProbe.ProbeAsync(cancellationToken);

        var data = new Dictionary<string, object>
        {
            ["durationMs"] = result.Duration.TotalMilliseconds
        };

        return result.Success
            ? HealthCheckResult.Healthy("PostgreSQL disponível (SELECT 1).", data)
            : HealthCheckResult.Unhealthy($"Falha ao consultar PostgreSQL: {result.Error}", data: data);
    }
}
