using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GCStock.Api.Design;

/// <summary>
/// Usada só pelas ferramentas `dotnet ef` (migrations). Evita subir a API inteira — e com ela a migração
/// automática e o seed de Development — só para gerar uma migration. A connection string é descartável:
/// gerar migration não abre conexão com o banco.
/// </summary>
public class GCStockDbContextDesignFactory : IDesignTimeDbContextFactory<GCStockDbContext>
{
    public GCStockDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<GCStockDbContext>()
            .UseNpgsql("Host=localhost;Database=gcstock_design_time;Username=design;Password=design")
            .Options);
}
