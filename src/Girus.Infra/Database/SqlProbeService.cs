using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Girus.Infra.Database;

public sealed class SqlProbeService(IConfiguration configuration) : IDatabaseProbe
{
    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection não configurada.");

    public async Task<DatabaseProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);

            stopwatch.Stop();
            return new DatabaseProbeResult(true, stopwatch.Elapsed, null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new DatabaseProbeResult(false, stopwatch.Elapsed, ex.Message);
        }
    }
}
