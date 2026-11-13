using GCStock.Application.Abstractions;
using GCStock.Application.Catalog;
using GCStock.Application.Pdv;
using GCStock.Domain.Entities;
using GCStock.Infra.Database;
using GCStock.Infra.Services;
using GCStock.IntegrationTests.TenantIsolation;
using Microsoft.EntityFrameworkCore;

namespace GCStock.IntegrationTests.Purchasing;

/// <summary>
/// Fase 5: FEFO, bloqueio de produto vencido, validade calculada de hoje, movimentação de ajuste e disponibilidade de kit.
/// Rodam contra PostgreSQL real (Testcontainers) e são ignorados quando o Docker não está disponível.
/// </summary>
public class StockFefoTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
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
            CompanyId = company.Id, Name = "Picanha", Sku = $"SKU-{suffix}", Code = $"C-{suffix}",
            Unit = "kg", Stock = 0, CostPrice = 20, SellingPrice = 100, AlertDays = 5,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return new World(new TestUser(company.Id, admin.Id), product);
    }

    /// <summary>Lote direto no banco (a compra não aceita validade no passado), somando ao estoque do produto.</summary>
    private async Task<StockBatch> AddLotAsync(World world, decimal quantity, DateOnly expiry, DateOnly entry, string lot)
    {
        await using var db = fixture.CreateContext();
        var batch = new StockBatch
        {
            CompanyId = world.User.CompanyId, ProductId = world.Product.Id, ProductName = world.Product.Name, Sku = world.Product.Code,
            LotNumber = lot, AvailableKg = quantity, UnitCost = 20m, Unit = "kg", EntryDate = entry, ExpiryDate = expiry,
            DaysRemaining = expiry.DayNumber - Today.DayNumber, Status = "novo",
        };
        db.StockBatches.Add(batch);
        var product = await db.Products.SingleAsync(p => p.Id == world.Product.Id);
        product.Stock += quantity;
        await db.SaveChangesAsync();
        return batch;
    }

    private PosService Pos(GCStockDbContext db, World world) =>
        new(db, world.User, new AuditRecorder(db, world.User), new AllowAllPermissions());

    private static FinalizeSaleRequest SaleOf(Product product, decimal quantity, Guid? lotId = null) =>
        new(quantity * 100m, "dinheiro", null, null, null, null, 0m, quantity * 100m,
            [new PosSaleItem("Picanha", quantity, "kg", 100m, ProductId: product.Id, StockBatchId: lotId)]);

    [DockerFact]
    public async Task Venda_consome_primeiro_o_lote_que_vence_antes_mesmo_que_tenha_entrado_depois()
    {
        var world = await SeedAsync();
        // Lote A entrou antes mas vence depois; lote B entrou depois e vence antes: FEFO manda sair o B.
        var lotA = await AddLotAsync(world, 5m, Today.AddDays(90), Today.AddDays(-10), "A");
        var lotB = await AddLotAsync(world, 5m, Today.AddDays(10), Today.AddDays(-1), "B");

        await using (var db = fixture.CreateContext())
        {
            await Pos(db, world).FinalizeSaleAsync(SaleOf(world.Product, 3m), CancellationToken.None);
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(2m, (await check.StockBatches.SingleAsync(b => b.Id == lotB.Id)).AvailableKg);
        Assert.Equal(5m, (await check.StockBatches.SingleAsync(b => b.Id == lotA.Id)).AvailableKg);
    }

    [DockerFact]
    public async Task Lote_vencido_nao_e_consumido_e_venda_acima_do_estoque_valido_e_recusada()
    {
        var world = await SeedAsync();
        await AddLotAsync(world, 4m, Today.AddDays(-2), Today.AddDays(-30), "VENCIDO");
        var valid = await AddLotAsync(world, 3m, Today.AddDays(20), Today.AddDays(-1), "VALIDO");

        await using (var db = fixture.CreateContext())
        {
            // 7 kg no estoque, mas só 3 kg válidos: pedir 5 kg não pode passar.
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Pos(db, world).FinalizeSaleAsync(SaleOf(world.Product, 5m), CancellationToken.None));
        }

        await using (var db = fixture.CreateContext())
        {
            await Pos(db, world).FinalizeSaleAsync(SaleOf(world.Product, 3m), CancellationToken.None);
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(0m, (await check.StockBatches.SingleAsync(b => b.Id == valid.Id)).AvailableKg);
        Assert.Equal(4m, (await check.StockBatches.SingleAsync(b => b.LotNumber == "VENCIDO")).AvailableKg);
    }

    [DockerFact]
    public async Task Lote_escolhido_na_venda_nao_pode_estar_vencido()
    {
        var world = await SeedAsync();
        var expired = await AddLotAsync(world, 4m, Today.AddDays(-1), Today.AddDays(-30), "VENCIDO");
        await AddLotAsync(world, 4m, Today.AddDays(20), Today.AddDays(-1), "VALIDO");

        await using var db = fixture.CreateContext();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Pos(db, world).FinalizeSaleAsync(SaleOf(world.Product, 1m, expired.Id), CancellationToken.None));
    }

    [DockerFact]
    public async Task Lote_que_vence_hoje_ainda_pode_ser_vendido()
    {
        var world = await SeedAsync();
        await AddLotAsync(world, 2m, Today, Today.AddDays(-10), "HOJE");

        await using (var db = fixture.CreateContext())
        {
            await Pos(db, world).FinalizeSaleAsync(SaleOf(world.Product, 1m), CancellationToken.None);
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(1m, (await check.Products.SingleAsync(p => p.Id == world.Product.Id)).Stock);
    }

    [DockerFact]
    public async Task Validade_do_produto_e_calculada_dos_lotes_com_saldo_de_hoje()
    {
        var world = await SeedAsync();
        await AddLotAsync(world, 5m, Today.AddDays(2), Today.AddDays(-10), "CRITICO");
        await AddLotAsync(world, 5m, Today.AddDays(60), Today.AddDays(-5), "LONGO");

        await using var db = fixture.CreateContext();
        var user = world.User;
        var service = new ProductService(db, user, new AuditRecorder(db, user), null!);

        var dto = (await service.GetAllAsync(CancellationToken.None)).Single(p => p.Id == world.Product.Id);
        Assert.Equal(Today.AddDays(2), dto.NearestExpiry);
        Assert.Equal(2, dto.ExpiryDaysRemaining);
        Assert.Equal("critico", dto.ExpiryStatus);
    }

    [DockerFact]
    public async Task Ajuste_manual_de_estoque_gera_movimentacao()
    {
        var world = await SeedAsync();

        await using (var db = fixture.CreateContext())
        {
            var user = world.User;
            var service = new ProductService(db, user, new AuditRecorder(db, user), new CategoryService(db, user, new AuditRecorder(db, user)));
            await service.SaveAsync(
                new SaveProductRequest(
                    Id: world.Product.Id, Sku: null, Ean: null, Name: null, Category: null, Unit: null, Stock: 12m, MinStock: null,
                    CostPrice: null, SellingPrice: null, IsPerishable: null, AlertDays: null, StorageTemp: null, NearestExpiry: null,
                    ExpiryDaysRemaining: null, ExpiryStatus: null, LotNumber: null, Supplier: null, ImageUrl: null, SubLocation: null,
                    IsPromo: null),
                CancellationToken.None);
        }

        await using var check = fixture.CreateContext();
        var movement = await check.StockMovements.SingleAsync(m => m.CompanyId == world.User.CompanyId && m.Type == "ajuste_estoque");
        Assert.Equal(12m, movement.AmountKg);
    }

    [DockerFact]
    public async Task Disponibilidade_do_kit_vem_do_estoque_dos_componentes()
    {
        var world = await SeedAsync();
        await AddLotAsync(world, 7m, Today.AddDays(30), Today, "K1");

        await using var db = fixture.CreateContext();
        var user = world.User;
        var kits = new KitService(db, user, new AuditRecorder(db, user));
        var component = new KitComponentDto(world.Product.Id, "x", "x", "", 2m, "kg", 0m, 0m, 0m);
        // StockAvailable informado (99) deve ser ignorado: 7 kg / 2 kg = 3 kits.
        var kit = await kits.SaveAsync(
            new SaveKitRequest(null, "Kit", null, null, null, 300m, null, null, null, null, null, [component], 99m), CancellationToken.None);

        Assert.Equal(3m, kit.StockAvailable);
        Assert.Equal("disponivel", kit.StockStatus);
    }
}
