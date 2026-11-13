using GCStock.Application.Abstractions;
using GCStock.Application.Catalog;
using GCStock.Application.Pdv;
using GCStock.Application.Purchasing;
using GCStock.Domain.Entities;
using GCStock.Infra.Database;
using GCStock.Infra.Services;
using GCStock.IntegrationTests.TenantIsolation;
using Microsoft.EntityFrameworkCore;

namespace GCStock.IntegrationTests.Purchasing;

/// <summary>
/// Fase 4: o custo dos kits acompanha o custo médio dos produtos (compra, cancelamento, ajuste manual) sem mexer no
/// preço de venda nem no custo gravado nas vendas. Rodam contra PostgreSQL real (Testcontainers); ignorados sem Docker.
/// </summary>
public class KitRecalculationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
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

    private sealed record World(TestUser User, Product Picanha, Product Linguica, Product Carvao);

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));

    /// <summary>Empresa com 3 produtos de custo 0; os custos vêm das compras de cada teste.</summary>
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

        Product NewProduct(string name, string tag) => new()
        {
            CompanyId = company.Id, Name = name, Sku = $"SKU-{tag}-{suffix}", Code = $"C-{tag}-{suffix}",
            Unit = "kg", Stock = 0, CostPrice = 0, SellingPrice = 50, AlertDays = 5,
        };
        var picanha = NewProduct("Picanha", "p");
        var linguica = NewProduct("Linguiça Aurora", "l");
        var carvao = NewProduct("Carvão", "c");
        db.Products.AddRange(picanha, linguica, carvao);
        await db.SaveChangesAsync();
        return new World(new TestUser(company.Id, admin.Id), picanha, linguica, carvao);
    }

    private PurchaseService Purchases(GCStockDbContext db, ICurrentUserContext user) => new(db, user, new AuditRecorder(db, user));
    private KitService Kits(GCStockDbContext db, ICurrentUserContext user) => new(db, user, new AuditRecorder(db, user));

    private async Task<PurchaseEntryDto> BuyAsync(World world, Product product, decimal quantity, decimal unitCost, string nfe)
    {
        await using var db = fixture.CreateContext();
        var item = new PurchaseItemDto(
            product.Name, product.Code, quantity, "kg", unitCost, quantity * unitCost, Today.AddDays(60), "L-" + nfe, true, null, product.Id);
        var request = new SavePurchaseRequest(nfe, "1", Today, "Fornecedor", null, null, null, null, null, null, null, "30 dias", "a_prazo", [item]);
        return await Purchases(db, world.User).SaveAsync(request, CancellationToken.None);
    }

    private static KitComponentDto Component(Product product, decimal quantity) =>
        new(product.Id, "ignorado", "ignorado", "", quantity, "kg", 9999m, 9999m, 0m);

    private async Task<KitDto> CreateKitAsync(World world, decimal price, params KitComponentDto[] components)
    {
        await using var db = fixture.CreateContext();
        // CostPrice/MarginPercent enviados pela tela devem ser ignorados quando há componentes.
        var request = new SaveKitRequest(null, "Fogo e Brasa Bronze", null, null, null, price, 1m, 99m, null, null, null, [.. components], null);
        return await Kits(db, world.User).SaveAsync(request, CancellationToken.None);
    }

    private async Task<KitDto> GetKitAsync(World world, Guid kitId)
    {
        await using var db = fixture.CreateContext();
        return (await Kits(db, world.User).GetAllAsync(CancellationToken.None)).Single(k => k.Id == kitId);
    }

    [DockerFact]
    public async Task Servidor_calcula_o_custo_do_kit_e_ignora_o_que_a_tela_enviou()
    {
        var world = await SeedAsync();
        await BuyAsync(world, world.Picanha, 10m, 60m, "A001");
        await BuyAsync(world, world.Linguica, 10m, 21m, "A002");

        var kit = await CreateKitAsync(world, 200m, Component(world.Picanha, 1m), Component(world.Linguica, 0.8m));

        Assert.Equal(76.8m, kit.CostPrice);
        Assert.Equal(61.6m, kit.MarginPercent);
        Assert.All(kit.Components, c => Assert.NotEqual(9999m, c.UnitCost));
        Assert.Contains(kit.Components, c => c.Name == "Picanha" && c.UnitCost == 60m);
    }

    [DockerFact]
    public async Task Nova_compra_recalcula_apenas_os_kits_que_usam_o_produto_e_preserva_o_preco()
    {
        var world = await SeedAsync();
        await BuyAsync(world, world.Picanha, 10m, 60m, "B001");
        await BuyAsync(world, world.Linguica, 10m, 21m, "B002");
        await BuyAsync(world, world.Carvao, 10m, 5m, "B003");
        var withLinguica = await CreateKitAsync(world, 100m, Component(world.Picanha, 1m), Component(world.Linguica, 1m));
        var withoutLinguica = await CreateKitAsync(world, 100m, Component(world.Picanha, 1m), Component(world.Carvao, 1m));
        Assert.Equal(81m, withLinguica.CostPrice);

        // 10 kg a R$ 21 + 10 kg a R$ 29 => custo médio da linguiça = R$ 25.
        await BuyAsync(world, world.Linguica, 10m, 29m, "B004");

        var after = await GetKitAsync(world, withLinguica.Id);
        Assert.Equal(85m, after.CostPrice);
        Assert.Equal(15m, after.MarginPercent);
        Assert.Equal(100m, after.SuggestedPrice);

        var untouched = await GetKitAsync(world, withoutLinguica.Id);
        Assert.Equal(withoutLinguica.CostPrice, untouched.CostPrice);

        await using var check = fixture.CreateContext();
        var history = await check.KitCostHistories.Where(h => h.KitId == withLinguica.Id).ToListAsync();
        var row = Assert.Single(history);
        Assert.Equal((81m, 85m), (row.PreviousCost, row.NewCost));
        Assert.Equal("recalculo_automatico", row.Source);
        Assert.Contains("Linguiça", row.TriggerProduct);
        Assert.False(await check.KitCostHistories.AnyAsync(h => h.KitId == withoutLinguica.Id));
    }

    [DockerFact]
    public async Task Cancelar_a_compra_devolve_o_custo_anterior_ao_kit()
    {
        var world = await SeedAsync();
        await BuyAsync(world, world.Linguica, 10m, 21m, "C001");
        var kit = await CreateKitAsync(world, 100m, Component(world.Linguica, 1m));
        var second = await BuyAsync(world, world.Linguica, 10m, 29m, "C002");
        Assert.Equal(25m, (await GetKitAsync(world, kit.Id)).CostPrice);

        await using (var db = fixture.CreateContext())
        {
            await Purchases(db, world.User).CancelAsync(second.Id, "lançada errada", CancellationToken.None);
        }

        Assert.Equal(21m, (await GetKitAsync(world, kit.Id)).CostPrice);
    }

    [DockerFact]
    public async Task Venda_ja_feita_mantem_o_custo_mesmo_depois_do_recalculo_do_kit()
    {
        var world = await SeedAsync();
        await BuyAsync(world, world.Linguica, 10m, 21m, "D001");
        var kit = await CreateKitAsync(world, 100m, Component(world.Linguica, 1m));

        await using (var db = fixture.CreateContext())
        {
            var sale = new FinalizeSaleRequest(
                100m, "dinheiro", null, null, null, null, 0m, 100m,
                [new PosSaleItem("Fogo e Brasa Bronze", 1m, "un", 100m, KitId: kit.Id)]);
            await new PosService(db, world.User, new AuditRecorder(db, world.User), new AllowAllPermissions())
                .FinalizeSaleAsync(sale, CancellationToken.None);
        }

        await BuyAsync(world, world.Linguica, 10m, 29m, "D002");
        Assert.Equal(25m, (await GetKitAsync(world, kit.Id)).CostPrice);

        await using var check = fixture.CreateContext();
        var item = await check.SaleItems.SingleAsync(i => i.KitId == kit.Id);
        Assert.Equal(21m, item.TotalCost);
    }

    [DockerFact]
    public async Task Kit_abaixo_da_margem_minima_e_sinalizado_com_o_preco_necessario()
    {
        var world = await SeedAsync();
        await BuyAsync(world, world.Linguica, 10m, 85m, "E001");

        // Custo 85 e preço 100 => margem 15%, abaixo dos 30% padrão da empresa.
        var kit = await CreateKitAsync(world, 100m, Component(world.Linguica, 1m));

        Assert.True(kit.IsBelowMinMargin);
        Assert.True(kit.UsesCompanyMinMargin);
        Assert.Equal(30m, kit.MinMarginPercent);
        Assert.Equal(121.43m, kit.PriceForMinMargin);
        Assert.Equal(21.43m, kit.PriceIncreaseNeeded);
        Assert.Equal(100m, kit.SuggestedPrice);
    }

    [DockerFact]
    public async Task Margem_minima_propria_do_kit_vale_mais_que_a_da_empresa()
    {
        var world = await SeedAsync();
        await BuyAsync(world, world.Linguica, 10m, 85m, "F001");

        await using var db = fixture.CreateContext();
        var request = new SaveKitRequest(
            null, "Kit", null, null, null, 100m, null, null, null, null, null, [Component(world.Linguica, 1m)], null, MinMarginPercent: 10m);
        var kit = await Kits(db, world.User).SaveAsync(request, CancellationToken.None);

        Assert.False(kit.IsBelowMinMargin);
        Assert.False(kit.UsesCompanyMinMargin);
        Assert.Equal(10m, kit.MinMarginPercent);
        Assert.Equal(0m, kit.PriceIncreaseNeeded);
    }

    [DockerFact]
    public async Task Ajuste_manual_do_custo_do_produto_recalcula_o_kit()
    {
        var world = await SeedAsync();
        await BuyAsync(world, world.Linguica, 10m, 20m, "G001");
        var kit = await CreateKitAsync(world, 100m, Component(world.Linguica, 1m));

        await using (var db = fixture.CreateContext())
        {
            var user = world.User;
            var products = new ProductService(db, user, new AuditRecorder(db, user), new CategoryService(db, user, new AuditRecorder(db, user)));
            await products.SaveAsync(
                new SaveProductRequest(
                    Id: world.Linguica.Id, Sku: null, Ean: null, Name: null, Category: null, Unit: null, Stock: null, MinStock: null,
                    CostPrice: 30m, SellingPrice: null, IsPerishable: null, AlertDays: null, StorageTemp: null, NearestExpiry: null,
                    ExpiryDaysRemaining: null, ExpiryStatus: null, LotNumber: null, Supplier: null, ImageUrl: null, SubLocation: null,
                    IsPromo: null),
                CancellationToken.None);
        }

        Assert.Equal(30m, (await GetKitAsync(world, kit.Id)).CostPrice);
    }

    [DockerFact]
    public async Task Historico_do_kit_so_lista_o_kit_da_propria_empresa()
    {
        var world = await SeedAsync();
        var other = await SeedAsync();
        await BuyAsync(other, other.Linguica, 10m, 20m, "H001");
        var otherKit = await CreateKitAsync(other, 100m, Component(other.Linguica, 1m));
        await BuyAsync(other, other.Linguica, 10m, 30m, "H002");

        await using var db = fixture.CreateContext();
        Assert.Empty(await Kits(db, world.User).GetCostHistoryAsync(otherKit.Id, CancellationToken.None));
    }
}
