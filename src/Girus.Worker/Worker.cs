using Girus.Infra.Database;
using Microsoft.Extensions.Configuration;

namespace Girus.Worker;

public class Worker(ILogger<Worker> logger, IDatabaseProbe databaseProbe, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = configuration.GetValue<int?>("Worker:DatabaseProbeIntervalSeconds") ?? 30;
        var interval = TimeSpan.FromSeconds(intervalSeconds);

        using var timer = new PeriodicTimer(interval);
        do
        {
            var result = await databaseProbe.ProbeAsync(stoppingToken);

            if (result.Success)
            {
                logger.LogInformation(
                    "Probe SQL bem-sucedido em {DurationMs}ms.",
                    result.Duration.TotalMilliseconds);
            }
            else
            {
                logger.LogError(
                    "Falha no probe SQL após {DurationMs}ms: {Error}",
                    result.Duration.TotalMilliseconds,
                    result.Error);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
