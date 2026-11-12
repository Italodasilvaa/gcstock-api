using System.Runtime.CompilerServices;

namespace GCStock.IntegrationTests;

/// <summary>
/// WebApplicationFactory sobe a API em Development, que aplica migrations e seed no banco dos user-secrets (o nprd da VPS).
/// Este inicializador roda antes de qualquer teste do assembly e desliga isso: testes nunca migram nem alteram o banco remoto.
/// </summary>
internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void DisableStartupMigration() =>
        Environment.SetEnvironmentVariable("Database__DisableStartupMigration", "true");
}
