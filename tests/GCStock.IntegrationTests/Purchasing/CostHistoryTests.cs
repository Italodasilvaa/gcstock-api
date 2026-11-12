using GCStock.Application.Abstractions;
using GCStock.Application.Pdv;
using GCStock.Application.Purchasing;
using GCStock.Domain.Entities;
using GCStock.Infra.Database;
using GCStock.Infra.Services;
using GCStock.IntegrationTests.TenantIsolation;
using Microsoft.EntityFrameworkCore;

namespace GCStock.IntegrationTests.Purchasing;

/// <summary>
/// Fase 3: custo médio ponderado com 4 casas, histórico de custo do produto e custo histórico das vendas.
/// Rodam contra PostgreSQL real (Testcontainers) e são ignorados quando o Docker não está disponível.
/// </summary>
public class CostHistoryTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
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

    private sealed record World(TestUser User, Product Product);

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));

    private async Task<World> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var admin = new Administrator { Name = "Dono", Email = $"dono-{suffix}@gcstock.test", PasswordHash = "x" };
        db.Administrators.Add(admin);
        await db.SaveChangesAsync();
        var company = new Company { CorporateName = $"Empresa {suffix}", TradeName = $"Empresa {suffix}", Document = "00.000.000/0001-00", OwnerId = admin.Id };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var product = new Product
        {
            CompanyId = company.Id, Name = "Linguiça Aurora", Sku = $"SKU-{suffix}", Code = $"C-{suffix}",
            Unit = "kg", Stock = 0, CostPrice = 0, SellingPrice = 40, AlertDays = 5,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return new World(new TestUser(company.Id, admin.Id), product);
    }

    private PurchaseService Purchases(GCStockDbContext db, ICurrentUserContext user) => new(db, user, new AuditRecorder(db, user));

    private async Task<PurchaseEntryDto> BuyAsync(World world, decimal quantity, decimal unitCost, string nfe)
    {
        await using var db = fixture.CreateContext();
        var item = new PurchaseItemDto(
            world.Product.Name, world.Product.Code, quantity, "kg", unitCost, quantity * unitCost, Today.AddDays(60), "L-" + nfe, true, null, world.Product.Id);
        var request = new SavePurchaseRequest(nfe, "1", Today, "Fornecedor", null, null, null, null, null, null, null, "30 dias", "a_prazo", [item]);
        return await Purchases(db, world.User).SaveAsync(request, CancellationToken.None);
    }

    private async Task<decimal> CostAsync(World world)
    {
        await using var db = fixture.CreateContext();
        return (await db.Products.SingleAsync(p => p.Id == world.Product.Id)).CostPrice;
    }

    private async Task<List<ProductCostHistory>> HistoryAsync(World world)
    {
        await using var db = fixture.CreateContext();
        return await db.ProductCostHistories
            .Where(h => h.ProductId == world.Product.Id)
            .OrderBy(h => h.CreatedAt)
            .ToListAsync();
    }

    [DockerFact]
    public async Task Custo_medio_considera_o_estoque_anterior()
    {
        var world = await SeedAsync();

        await BuyAsync(world, 10m, 20m, "9001");
        await BuyAsync(world, 10m, 30m, "9002");

        Assert.Equal(25m, await CostAsync(world));
        var history = await HistoryAsync(world);
        Assert.Equal(2, history.Count);
        Assert.Equal((0m, 20m), (history[0].PreviousCost, history[0].NewCost));
        Assert.Equal((20m, 25m), (history[1].PreviousCost, history[1].NewCost));
        Assert.All(history, h => Assert.Equal("compra", h.Source));
        Assert.All(history, h => Assert.NotNull(h.PurchaseCode));
    }

    [DockerFact]
    public async Task Custo_medio_guarda_4_casas_sem_arredondar_para_centavos()
    {
        var world = await SeedAsync();

        await BuyAsync(world, 3m, 10m, "9101");
        await BuyAsync(world, 1m, 10.01m, "9102");

        // (3 x 10,00 + 1 x 10,01) / 4 = 10,0025 — com 2 casas viraria 10,00 e a diferença se acumularia a cada compra.
        Assert.Equal(10.0025m, await CostAsync(world));
    }

    [DockerFact]
    public async Task Cancelar_a_ultima_compra_devolve_o_custo_anterior_e_registra_no_historico()
    {
        var world = await SeedAsync();
        await BuyAsync(world, 10m, 20m, "9201");
        var second = await BuyAsync(world, 10m, 30m, "9202");
        Assert.Equal(25m, await CostAsync(world));

        await using (var db = fixture.CreateContext())
        {
            await Purchases(db, world.User).CancelAsync(second.Id, "lançada errada", CancellationToken.None);
        }

        Assert.Equal(20m, await CostAsync(world));
        var last = (await HistoryAsync(world)).Last();
        Assert.Equal("cancelamento_compra", last.Source);
        Assert.Equal((25m, 20m), (last.PreviousCost, last.NewCost));
    }

    [DockerFact]
    public async Task Mudar_o_custo_depois_nao_altera_o_lucro_historico_da_venda()
    {
        var world = await SeedAsync();
        await BuyAsync(world, 10m, 20m, "9301");

        await using (var db = fixture.CreateContext())
        {
            var sale = new FinalizeSaleRequest(
                40m, "dinheiro", null, null, null, null, 0m, 40m,
                [new PosSaleItem("Linguiça Aurora", 1m, "kg", 40m, ProductId: world.Product.Id)]);
            await new PosService(db, world.User, new AuditRecorder(db, world.User), new AllowAllPermissions())
                .FinalizeSaleAsync(sale, CancellationToken.None);
        }

        // Nova compra mais cara muda o custo médio, mas não a venda que já aconteceu.
        await BuyAsync(world, 10m, 30m, "9302");
        Assert.NotEqual(20m, await CostAsync(world));

        await using var check = fixture.CreateContext();
        var item = await check.SaleItems.SingleAsync(i => i.ProductId == world.Product.Id);
        Assert.Equal(20m, item.UnitCost);
        Assert.Equal(20m, item.TotalCost);
    }

    [DockerFact]
    public async Task Historico_so_lista_o_produto_da_propria_empresa()
    {
        var world = await SeedAsync();
        var other = await SeedAsync();
        await BuyAsync(other, 5m, 12m, "9401");

        await using var db = fixture.CreateContext();
        var service = new ProductService(db, world.User, new AuditRecorder(db, world.User), null!);

        Assert.Empty(await service.GetCostHistoryAsync(other.Product.Id, CancellationToken.None));
    }
}
