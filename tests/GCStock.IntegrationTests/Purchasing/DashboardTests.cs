using GCStock.Application.Abstractions;
using GCStock.Application.Catalog;
using GCStock.Application.Pdv;
using GCStock.Application.Sales;
using GCStock.Domain.Entities;
using GCStock.Infra.Database;
using GCStock.Infra.Services;
using GCStock.IntegrationTests.TenantIsolation;
using Microsoft.EntityFrameworkCore;

namespace GCStock.IntegrationTests.Purchasing;

/// <summary>
/// Fase 9: indicadores do painel de dados reais (faturamento por período, meta, ticket, ranking, financeiro, estoque e kits).
/// Rodam contra PostgreSQL real (Testcontainers); ignorados sem Docker.
/// </summary>
public class DashboardTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private sealed class TestUser(Guid companyId, Guid administratorId) : ICurrentUserContext
    {
        public bool IsAuthenticated => true;
        public Guid UserId { get; } = administratorId;
        public string UserName => "Teste";
        public string UserEmail => "teste@gcstock.test";
        public string Role => "admin";
        public Guid CompanyId { get; } = companyId;
        public Guid? AdministratorId { get; } = administratorId;
        public Guid? EmployeeId => null;
        public bool IsOwner => true;
    }

    private sealed class AllowAllPermissions : IPermissionChecker
    {
        public Task<bool> HasAsync(string permission, CancellationToken ct) => Task.FromResult(true);
        public Task<List<string>> GetCurrentAsync(CancellationToken ct) => Task.FromResult(new List<string>());
    }

    private sealed record World(TestUser User, Product Picanha, Product Linguica, Product Parado);

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));

    private async Task<World> SeedAsync(decimal goal = 3000m)
    {
        await using var db = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var admin = new Administrator { Name = "Dono", Email = $"dono-{suffix}@gcstock.test", PasswordHash = "x" };
        db.Administrators.Add(admin);
        await db.SaveChangesAsync();
        var company = new Company { CorporateName = $"Empresa {suffix}", TradeName = $"Empresa {suffix}", Document = "00.000.000/0001-00", OwnerId = admin.Id };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        db.AlertSettings.Add(new AlertSetting { CompanyId = company.Id, MonthlyRevenueGoal = goal });

        Product NewProduct(string name, string tag, decimal stock, decimal cost, decimal min) => new()
        {
            CompanyId = company.Id, Name = name, Sku = $"SKU-{tag}-{suffix}", Code = $"C-{tag}-{suffix}", Category = "Carnes",
            Unit = "kg", Stock = stock, MinStock = min, CostPrice = cost, SellingPrice = 100, AlertDays = 5,
        };
        var picanha = NewProduct("Picanha", "p", 50, 40, 10);
        var linguica = NewProduct("Linguiça", "l", 3, 20, 10);   // abaixo do mínimo
        var parado = NewProduct("Carvão", "c", 20, 5, 5);        // sem vendas
        db.Products.AddRange(picanha, linguica, parado);
        await db.SaveChangesAsync();
        return new World(new TestUser(company.Id, admin.Id), picanha, linguica, parado);
    }

    private PosService Pos(GCStockDbContext db, World world) =>
        new(db, world.User, new AuditRecorder(db, world.User), new AllowAllPermissions());

    private async Task<FinalizeSaleResult> SellAsync(World world, Product product, decimal kg, decimal price = 100m)
    {
        await using var db = fixture.CreateContext();
        return await Pos(db, world).FinalizeSaleAsync(new FinalizeSaleRequest(
            kg * price, "dinheiro", null, null, null, null, 0m, kg * price,
            [new PosSaleItem(product.Name, kg, "kg", price, ProductId: product.Id)]), CancellationToken.None);
    }

    private async Task<GCStock.Application.Dashboard.DashboardDto> DashboardAsync(World world)
    {
        await using var db = fixture.CreateContext();
        var user = world.User;
        var audit = new AuditRecorder(db, user);
        var service = new DashboardService(
            db, user, new FinancialSummaryService(db, user), new CashService(db, user, audit), new KitService(db, user, audit),
            new ProductService(db, user, audit, null!));
        return await service.GetAsync(Today, CancellationToken.None);
    }

    [DockerFact]
    public async Task Faturamento_ticket_medio_e_meta_vem_das_vendas_reais()
    {
        var world = await SeedAsync(goal: 1000m);
        await SellAsync(world, world.Picanha, 2m);   // 200
        await SellAsync(world, world.Picanha, 3m);   // 300

        GCStock.Application.Dashboard.DashboardDto dash = await DashboardAsync(world);

        Assert.Equal(500m, dash.Commercial.Day.Revenue);
        Assert.Equal(500m, dash.Commercial.Month.Revenue);
        Assert.Equal(2, dash.Commercial.Month.SalesCount);
        Assert.Equal(250m, dash.Commercial.AverageTicket);
        Assert.Equal((1000m, 500m, 500m, 50m), (dash.Commercial.Goal.Target, dash.Commercial.Goal.Realized, dash.Commercial.Goal.Remaining, dash.Commercial.Goal.PercentAchieved));
        Assert.Equal("Picanha", Assert.Single(dash.Commercial.TopProducts).Name);
        Assert.Equal(5m, dash.Commercial.TopProducts[0].Qty);
        Assert.Equal("Carnes", Assert.Single(dash.Commercial.ByCategory).Category);
    }

    [DockerFact]
    public async Task Devolucao_reduz_o_faturamento_e_venda_cancelada_nao_conta()
    {
        var world = await SeedAsync();
        var cancelled = await SellAsync(world, world.Picanha, 4m);
        var kept = await SellAsync(world, world.Picanha, 3m);

        await using (var db = fixture.CreateContext())
        {
            var sales = new SaleService(db, world.User, new AuditRecorder(db, world.User));
            await sales.CancelAsync(cancelled.SaleId, "teste", CancellationToken.None);
        }
        await using (var db = fixture.CreateContext())
        {
            var itemId = await db.SaleItems.Where(i => i.SaleRecord!.SaleId == kept.SaleId).Select(i => i.Id).SingleAsync();
            var sales = new SaleService(db, world.User, new AuditRecorder(db, world.User));
            await sales.ReturnItemsAsync(kept.SaleId, new ReturnSaleRequest("devolvido", [new ReturnSaleLine(itemId, 1m, true)]), CancellationToken.None);
        }

        GCStock.Application.Dashboard.DashboardDto dash = await DashboardAsync(world);

        // 300 vendidos (a venda cancelada não conta) menos 100 devolvidos.
        Assert.Equal(200m, dash.Commercial.Month.Revenue);
        Assert.Equal(1, dash.Commercial.Month.SalesCount);
    }

    [DockerFact]
    public async Task Estoque_mostra_baixo_zerado_vencido_parado_e_valor()
    {
        var world = await SeedAsync();
        await SellAsync(world, world.Picanha, 2m);

        await using (var db = fixture.CreateContext())
        {
            // Lote vencido da picanha e produto zerado.
            db.StockBatches.Add(new StockBatch
            {
                CompanyId = world.User.CompanyId, ProductId = world.Picanha.Id, ProductName = "Picanha", Sku = world.Picanha.Code, LotNumber = "VENC",
                AvailableKg = 4m, UnitCost = 40m, Unit = "kg", EntryDate = Today.AddDays(-40), ExpiryDate = Today.AddDays(-2), DaysRemaining = -2, Status = "vencido",
            });
            var zero = new Product
            {
                CompanyId = world.User.CompanyId, Name = "Zerado", Sku = "Z-" + Guid.NewGuid().ToString("N")[..6], Code = "Z", Unit = "kg", Stock = 0, MinStock = 5, CostPrice = 1, SellingPrice = 2,
            };
            db.Products.Add(zero);
            await db.SaveChangesAsync();
        }

        GCStock.Application.Dashboard.DashboardDto dash = await DashboardAsync(world);

        Assert.Contains(dash.Stock.LowStock, p => p.Name == "Linguiça");
        Assert.Contains(dash.Stock.OutOfStock, p => p.Name == "Zerado");
        Assert.Equal(4m, Assert.Single(dash.Stock.Expired).Qty);
        Assert.Contains(dash.Stock.Stagnant, p => p.Name == "Carvão");
        Assert.DoesNotContain(dash.Stock.Stagnant, p => p.Name == "Picanha");
        // 48 kg de picanha a R$ 40 + 3 kg de linguiça a R$ 20 + 20 kg de carvão a R$ 5.
        Assert.Equal(48m * 40m + 3m * 20m + 20m * 5m, dash.Stock.StockValue);
    }

    [DockerFact]
    public async Task Kits_mostram_margem_alerta_mais_vendido_e_peso_do_custo()
    {
        var world = await SeedAsync();

        Guid kitId;
        await using (var db = fixture.CreateContext())
        {
            var user = world.User;
            var components = new List<KitComponentDto>
            {
                new(world.Picanha.Id, "x", "x", "", 1m, "kg", 0m, 0m, 0m),
                new(world.Parado.Id, "x", "x", "", 2m, "kg", 0m, 0m, 0m),
            };
            // Custo: 40 + 2 x 5 = 50; preço 60 => margem 16,7%, abaixo dos 30% padrão.
            var kit = await new KitService(db, user, new AuditRecorder(db, user)).SaveAsync(
                new SaveKitRequest(null, "Fogo e Brasa", null, null, null, 60m, null, null, null, null, null, components, null), CancellationToken.None);
            kitId = kit.Id;
        }

        await using (var db = fixture.CreateContext())
        {
            await Pos(db, world).FinalizeSaleAsync(new FinalizeSaleRequest(
                120m, "dinheiro", null, null, null, null, 0m, 120m, [new PosSaleItem("Fogo e Brasa", 2m, "un", 60m, KitId: kitId)]), CancellationToken.None);
        }

        GCStock.Application.Dashboard.DashboardDto dash = await DashboardAsync(world);

        var row = Assert.Single(dash.Kits.Kits);
        Assert.True(row.IsBelowMinMargin);
        Assert.Equal(1, dash.Kits.BelowMarginCount);
        Assert.Equal(("Fogo e Brasa", 2m), (dash.Kits.BestSeller!.Name, dash.Kits.BestSeller.Qty));
        // Lucro do mês: receita 120 menos custo gravado 2 x 50.
        Assert.Equal(20m, dash.Kits.MostProfitable!.Profit);
        Assert.Equal(2, dash.Commercial.KitsSold);
        Assert.Equal("Picanha", dash.Kits.CostDrivers[0].Product);
        Assert.Equal(80m, dash.Kits.CostDrivers[0].SharePercent);
    }

    [DockerFact]
    public async Task Dados_de_outra_empresa_nao_aparecem_no_painel()
    {
        var world = await SeedAsync();
        var other = await SeedAsync();
        await SellAsync(other, other.Picanha, 5m);

        GCStock.Application.Dashboard.DashboardDto dash = await DashboardAsync(world);

        Assert.Equal(0m, dash.Commercial.Month.Revenue);
        Assert.Empty(dash.Commercial.TopProducts);
    }
}
