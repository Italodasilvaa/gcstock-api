using Girus.Infra.Database;
using Girus.Infra.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

namespace Girus.Infra.DependencyInjection;

public static class InfraServiceCollectionExtensions
{
    public static IServiceCollection AddInfra(this IServiceCollection services)
    {
        services.AddSingleton<IDatabaseProbe, SqlProbeService>();

        services
            .AddHealthChecks()
            .AddCheck<SqlServerHealthCheck>("database", tags: ["ready"]);

        return services;
    }
}
