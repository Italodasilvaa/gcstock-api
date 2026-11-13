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
/// Fase 6B: devolução parcial de venda: estoque (apto volta, não apto vira perda), financeiro (abate em aberto, resto sai do
/// caixa), DRE e a venda original preservada. Rodam contra PostgreSQL real (Testcontainers); ignorados sem Docker.
/// </summary>
public class SaleReturnTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
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

    private sealed record World(TestUser User, Product Product, Client Client);

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));

    /// <summary>Empresa com picanha (R$ 100/kg, custo 40) e 20 kg em um lote, mais um cliente.</summary>
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
            Unit = "kg", Stock = 20, CostPrice = 40, SellingPrice = 100, AlertDays = 5,
        };
        var client = new Client { CompanyId = company.Id, Name = "Cliente Fiado" };
        db.AddRange(product, client);
        await db.SaveChangesAsync();
        db.StockBatches.Add(new StockBatch
        {
            CompanyId = company.Id, ProductId = product.Id, ProductName = product.Name, Sku = product.Code, LotNumber = "L1",
            AvailableKg = 20, UnitCost = 40, Unit = "kg", EntryDate = Today.AddDays(-5), ExpiryDate = Today.AddDays(60),
            DaysRemaining = 60, Status = "novo",
        });
        await db.SaveChangesAsync();
        return new World(new TestUser(company.Id, admin.Id), product, client);
    }

    private PosService Pos(GCStockDbContext db, World world) =>
        new(db, world.User, new AuditRecorder(db, world.User), new AllowAllPermissions());

    private SaleService Sales(GCStockDbContext db, World world) => new(db, world.User, new AuditRecorder(db, world.User));

    private async Task<(FinalizeSaleResult Result, Guid ItemId)> SellAsync(World world, decimal quantity, string mode = "dinheiro", int installments = 1)
    {
        FinalizeSaleResult result;
        await using (var db = fixture.CreateContext())
        {
            var request = new FinalizeSaleRequest(
                quantity * 100m, mode, null, mode == "a_prazo" ? world.Client.Id : null, mode == "a_prazo" ? world.Client.Name : null,
                mode == "a_prazo" ? DateOnly.FromDateTime(DateTime.Today.AddDays(30)) : null, 0m, quantity * 100m,
                [new PosSaleItem("Picanha", quantity, "kg", 100m, ProductId: world.Product.Id)], Installments: installments);
            result = await Pos(db, world).FinalizeSaleAsync(request, CancellationToken.None);
        }

        await using var check = fixture.CreateContext();
        var itemId = await check.SaleItems.Where(i => i.SaleRecord!.SaleId == result.SaleId).Select(i => i.Id).SingleAsync();
        return (result, itemId);
    }

    private static ReturnSaleRequest Return(Guid itemId, decimal qty, bool restock, string reason = "cliente desistiu", decimal? refund = null) =>
        new(reason, [new ReturnSaleLine(itemId, qty, restock)], refund);

    [DockerFact]
    public async Task Devolucao_parcial_apta_volta_ao_estoque_e_ao_lote_e_preserva_a_venda()
    {
        var world = await SeedAsync();
        var (sale, itemId) = await SellAsync(world, 3m);

        SaleHistoryDto dto;
        await using (var db = fixture.CreateContext())
        {
            dto = (await Sales(db, world).ReturnItemsAsync(sale.SaleId, Return(itemId, 1m, restock: true), CancellationToken.None))!;
        }

        Assert.Equal("concluida", dto.Status);
        Assert.Equal(100m, dto.ReturnedAmount);
        Assert.Equal(1m, dto.Items.Single().ReturnedQty);
        Assert.Equal(3m, dto.Items.Single().Qty);

        await using var check = fixture.CreateContext();
        Assert.Equal(18m, (await check.Products.SingleAsync(p => p.Id == world.Product.Id)).Stock);
        Assert.Equal(18m, (await check.StockBatches.SingleAsync(b => b.ProductId == world.Product.Id)).AvailableKg);
        Assert.Contains(await check.StockMovements.Where(m => m.CompanyId == world.User.CompanyId).ToListAsync(),
            m => m.Type == "devolucao_venda" && m.AmountKg == 1m);

        // A venda original continua intacta.
        var original = await check.SaleRecords.Include(s => s.Items).SingleAsync(s => s.SaleId == sale.SaleId);
        Assert.Equal(300m, original.Total);
        Assert.Equal(3m, original.Items.Single().Qty);
    }

    [DockerFact]
    public async Task Item_sem_condicao_de_revenda_nao_volta_ao_estoque_e_vira_perda()
    {
        var world = await SeedAsync();
        var (sale, itemId) = await SellAsync(world, 2m);

        await using (var db = fixture.CreateContext())
        {
            await Sales(db, world).ReturnItemsAsync(sale.SaleId, Return(itemId, 1m, restock: false, reason: "produto estragado"), CancellationToken.None);
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(18m, (await check.Products.SingleAsync(p => p.Id == world.Product.Id)).Stock);
        var loss = await check.StockLosses.SingleAsync(l => l.CompanyId == world.User.CompanyId);
        Assert.Equal((1m, 40m), (loss.Qty, loss.TotalCost));
        Assert.False(await check.StockMovements.AnyAsync(m => m.CompanyId == world.User.CompanyId && m.Type == "devolucao_venda"));
    }

    [DockerFact]
    public async Task Nao_devolve_mais_do_que_foi_vendido_nem_em_devolucoes_sucessivas()
    {
        var world = await SeedAsync();
        var (sale, itemId) = await SellAsync(world, 3m);

        await using (var db = fixture.CreateContext())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Sales(db, world).ReturnItemsAsync(sale.SaleId, Return(itemId, 4m, true), CancellationToken.None));
        }
        await using (var db = fixture.CreateContext())
        {
            await Sales(db, world).ReturnItemsAsync(sale.SaleId, Return(itemId, 2m, true), CancellationToken.None);
        }
        await using (var db = fixture.CreateContext())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Sales(db, world).ReturnItemsAsync(sale.SaleId, Return(itemId, 2m, true), CancellationToken.None));
            await Sales(db, world).ReturnItemsAsync(sale.SaleId, Return(itemId, 1m, true), CancellationToken.None);
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(20m, (await check.Products.SingleAsync(p => p.Id == world.Product.Id)).Stock);
        Assert.Equal(20m, (await check.StockBatches.SingleAsync(b => b.ProductId == world.Product.Id)).AvailableKg);
    }

    [DockerFact]
    public async Task Devolucao_exige_motivo_e_nao_passa_do_valor_pago()
    {
        var world = await SeedAsync();
        var (sale, itemId) = await SellAsync(world, 2m);

        await using var db = fixture.CreateContext();
        await Assert.ThrowsAsync<ArgumentException>(
            () => Sales(db, world).ReturnItemsAsync(sale.SaleId, Return(itemId, 1m, true, reason: " "), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(
            () => Sales(db, world).ReturnItemsAsync(sale.SaleId, Return(itemId, 1m, true, refund: 500m), CancellationToken.None));
    }

    [DockerFact]
    public async Task Venda_a_prazo_em_aberto_tem_o_titulo_abatido_e_nada_sai_do_caixa()
    {
        var world = await SeedAsync();
        var (sale, itemId) = await SellAsync(world, 3m, mode: "a_prazo", installments: 3);

        await using (var db = fixture.CreateContext())
        {
            await Sales(db, world).ReturnItemsAsync(sale.SaleId, Return(itemId, 1m, true), CancellationToken.None);
        }

        await using var check = fixture.CreateContext();
        var parts = await check.AccountsReceivables.Where(r => r.SaleId == sale.SaleId).OrderBy(r => r.InstallmentNumber).ToListAsync();
        // R$ 100 devolvidos saem da última parcela (R$ 100): ela some e as outras duas ficam.
        Assert.Equal(2, parts.Count);
        Assert.Equal(200m, parts.Sum(p => p.GrossAmount));

        var saleReturn = await check.SaleReturns.SingleAsync(r => r.SaleRecordId == check.SaleRecords.Single(s => s.SaleId == sale.SaleId).Id);
        Assert.Equal((100m, 100m, 0m), (saleReturn.RefundAmount, saleReturn.ReceivableAbated, saleReturn.CashRefundAmount));
        var clientDto = (await new ClientService(check, world.User, new AuditRecorder(check, world.User)).GetAllAsync(CancellationToken.None))
            .Single(c => c.Id == world.Client.Id);
        Assert.Equal((200m, 200m), (clientDto.TotalSpent, clientDto.OpenBalance));
    }

    [DockerFact]
    public async Task Venda_recebida_na_hora_devolve_dinheiro_do_caixa_e_a_dre_reflete_a_devolucao()
    {
        var world = await SeedAsync();
        var (sale, itemId) = await SellAsync(world, 3m);

        await using (var db = fixture.CreateContext())
        {
            await Sales(db, world).ReturnItemsAsync(sale.SaleId, Return(itemId, 1m, true), CancellationToken.None);
        }

        await using var check = fixture.CreateContext();
        var user = world.User;
        var cash = await new CashService(check, user, new AuditRecorder(check, user)).GetAsync(Today.AddDays(-1), Today.AddDays(1), CancellationToken.None);
        Assert.Contains(cash.Movements, m => m.Kind == "devolucao_venda" && m.Amount == -100m);
        // Entrou 300 da venda e saiu 100 devolvidos.
        Assert.Equal(200m, cash.ClosingBalance);

        var dre = await new FinancialSummaryService(check, user).GetAsync(Today.AddDays(-1), Today.AddDays(1), CancellationToken.None);
        Assert.Equal(200m, dre.NetRevenue);
        // Custo: 3 kg x 40 vendidos menos 1 kg x 40 que voltou ao estoque.
        Assert.Equal(80m, dre.CostOfGoodsSold);
    }

    [DockerFact]
    public async Task Venda_com_devolucao_nao_pode_ser_cancelada()
    {
        var world = await SeedAsync();
        var (sale, itemId) = await SellAsync(world, 3m);

        await using (var db = fixture.CreateContext())
        {
            await Sales(db, world).ReturnItemsAsync(sale.SaleId, Return(itemId, 1m, true), CancellationToken.None);
        }

        await using (var db = fixture.CreateContext())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Sales(db, world).CancelAsync(sale.SaleId, "teste", CancellationToken.None));
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(18m, (await check.Products.SingleAsync(p => p.Id == world.Product.Id)).Stock);
    }

    [DockerFact]
    public async Task Devolucao_de_kit_apta_devolve_os_componentes()
    {
        var world = await SeedAsync();

        Guid kitId;
        await using (var db = fixture.CreateContext())
        {
            var user = world.User;
            var component = new KitComponentDto(world.Product.Id, "x", "x", "", 2m, "kg", 0m, 0m, 0m);
            var kit = await new KitService(db, user, new AuditRecorder(db, user)).SaveAsync(
                new SaveKitRequest(null, "Kit", null, null, null, 300m, null, null, null, null, null, [component], null), CancellationToken.None);
            kitId = kit.Id;
        }

        FinalizeSaleResult sale;
        await using (var db = fixture.CreateContext())
        {
            sale = await Pos(db, world).FinalizeSaleAsync(new FinalizeSaleRequest(
                600m, "dinheiro", null, null, null, null, 0m, 600m, [new PosSaleItem("Kit", 2m, "un", 300m, KitId: kitId)]), CancellationToken.None);
        }
        Guid itemId;
        await using (var db = fixture.CreateContext())
        {
            itemId = await db.SaleItems.Where(i => i.SaleRecord!.SaleId == sale.SaleId).Select(i => i.Id).SingleAsync();
            Assert.Equal(16m, (await db.Products.SingleAsync(p => p.Id == world.Product.Id)).Stock);
        }

        await using (var db = fixture.CreateContext())
        {
            // Devolve 1 dos 2 kits: 2 kg de picanha voltam.
            await Sales(db, world).ReturnItemsAsync(sale.SaleId, Return(itemId, 1m, true), CancellationToken.None);
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(18m, (await check.Products.SingleAsync(p => p.Id == world.Product.Id)).Stock);
    }

    [DockerFact]
    public async Task Devolucao_de_venda_de_outra_empresa_nao_e_encontrada()
    {
        var world = await SeedAsync();
        var other = await SeedAsync();
        var (sale, itemId) = await SellAsync(other, 2m);

        await using var db = fixture.CreateContext();
        Assert.Null(await Sales(db, world).ReturnItemsAsync(sale.SaleId, Return(itemId, 1m, true), CancellationToken.None));
    }
}
