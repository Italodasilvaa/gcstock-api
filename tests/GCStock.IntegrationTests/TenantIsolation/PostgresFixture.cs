using System.Diagnostics;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace GCStock.IntegrationTests.TenantIsolation;

/// <summary>
/// [Fact] que só roda quando há um daemon Docker disponível (os testes sobem um PostgreSQL real via Testcontainers,
/// porque FOR UPDATE e transações não são reproduzidos por banco em memória). Sem Docker, o teste é ignorado
/// em vez de falhar, com o motivo no resultado.
/// </summary>
public sealed class DockerFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> DockerAvailable = new(CheckDocker);

    public DockerFactAttribute()
    {
        if (!DockerAvailable.Value)
        {
            Skip = "Docker não está disponível: inicie o Docker Desktop para executar os testes com PostgreSQL real.";
        }
    }

    private static bool CheckDocker()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null) return false;
            if (!process.WaitForExit(10_000))
            {
                process.Kill();
                return false;
            }
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>Um container PostgreSQL por classe de teste, com todas as migrations aplicadas.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        // Sem Docker os testes são ignorados (DockerFact); nada a subir.
        var probe = new DockerFactAttribute();
        if (probe.Skip is not null) return;

        _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public GCStockDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<GCStockDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}
