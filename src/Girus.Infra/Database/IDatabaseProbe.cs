namespace Girus.Infra.Database;

public interface IDatabaseProbe
{
    Task<DatabaseProbeResult> ProbeAsync(CancellationToken cancellationToken = default);
}

public sealed record DatabaseProbeResult(bool Success, TimeSpan Duration, string? Error);
