using GCStock.Application.Abstractions;
using GCStock.Application.Finance;
using GCStock.Application.Pdv;
using GCStock.Domain.Entities;
using GCStock.Infra.Database;
using GCStock.Infra.Services;
using GCStock.IntegrationTests.TenantIsolation;
using Microsoft.EntityFrameworkCore;

namespace GCStock.IntegrationTests.Purchasing;

/// <summary>
/// Fase 7: antecipação de cartão configurável por modalidade, com custo na DRE (despesa financeira) e no caixa.
/// Rodam contra PostgreSQL real (Testcontainers); ignorados sem Docker.
/// </summary>
public class CardAnticipationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
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

    private sealed record World(TestUser User, Product Product, PaymentFeeSetting Card);

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));

    /// <summary>Empresa com picanha (R$ 100/kg, 50 kg) e cartão parcelado de 2% que antecipa a 4%.</summary>
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
            Unit = "kg", Stock = 50, CostPrice = 40, SellingPrice = 100, AlertDays = 5,
        };
        var card = new PaymentFeeSetting
        {
            CompanyId = company.Id, Method = "Cartão de Crédito Parcelado", FeePercent = 2m, AnticipationFeePercent = 4m, SortOrder = 1,
        };
        db.AddRange(product, card);
        await db.SaveChangesAsync();
        return new World(new TestUser(company.Id, admin.Id), product, card);
    }

    private async Task<FinalizeSaleResult> SellInstallmentsAsync(World world, int installments)
    {
        await using var db = fixture.CreateContext();
        var pos = new PosService(db, world.User, new AuditRecorder(db, world.User), new AllowAllPermissions());
        // 3 kg a R$ 100 com taxa de 2% repassada: total 306, líquido 300.
        var request = new FinalizeSaleRequest(
            306m, "parcelado", null, null, null, null, 0m, 0m,
            [new PosSaleItem("Picanha", 3m, "kg", 100m, ProductId: world.Product.Id)],
            PaymentOptionId: world.Card.Id, Installments: installments);
        return await pos.FinalizeSaleAsync(request, CancellationToken.None);
    }

    private CardSettlementService Cards(GCStockDbContext db, World world) => new(db, world.User, new AuditRecorder(db, world.User));

    [DockerFact]
    public async Task Liquidacao_de_cartao_lista_as_parcelas_reais_da_venda()
    {
        var world = await SeedAsync();
        var sale = await SellInstallmentsAsync(world, 3);

        await using var db = fixture.CreateContext();
        var list = await Cards(db, world).GetAllAsync(CancellationToken.None);

        Assert.Equal(3, list.Count);
        Assert.All(list, c => Assert.Equal("a_liquidar", c.Status));
        Assert.Equal(sale.Total, list.Sum(c => c.GrossAmount));
        Assert.Equal(sale.NetAmount, list.Sum(c => c.NetAmount));
        Assert.All(list, c => Assert.Equal(Today, c.SaleDate));
    }

    [DockerFact]
    public async Task Antecipar_usa_a_taxa_da_modalidade_e_recebe_hoje_o_liquido_menos_o_custo()
    {
        var world = await SeedAsync();
        var sale = await SellInstallmentsAsync(world, 3);

        Guid titleId;
        await using (var db = fixture.CreateContext())
        {
            titleId = (await db.AccountsReceivables.Where(r => r.SaleId == sale.SaleId).OrderBy(r => r.InstallmentNumber).FirstAsync()).Id;
            await Cards(db, world).AnticipateAsync(titleId, CancellationToken.None);
        }

        await using var check = fixture.CreateContext();
        var title = await check.AccountsReceivables.SingleAsync(r => r.Id == titleId);
        // Parcela líquida de R$ 100,00 a 4% = R$ 4,00.
        Assert.Equal((4m, "recebido", Today), (title.AnticipationFee, title.Status, title.ReceivedDate));
        Assert.Equal(Today, title.AnticipatedAt);

        var settlement = (await Cards(check, world).GetAllAsync(CancellationToken.None)).Single(c => c.Id == titleId);
        Assert.Equal("antecipado", settlement.Status);
        Assert.Equal(4m, settlement.AnticipatedFee);
        Assert.Equal(96m, settlement.NetAmount);
    }

    [DockerFact]
    public async Task Custo_da_antecipacao_reduz_o_caixa_recebido_e_o_resultado_da_dre()
    {
        var world = await SeedAsync();
        var sale = await SellInstallmentsAsync(world, 3);

        await using (var db = fixture.CreateContext())
        {
            var first = await db.AccountsReceivables.Where(r => r.SaleId == sale.SaleId).OrderBy(r => r.InstallmentNumber).FirstAsync();
            await Cards(db, world).AnticipateAsync(first.Id, CancellationToken.None);
        }

        await using var check = fixture.CreateContext();
        var user = world.User;
        var cash = await new CashService(check, user, new AuditRecorder(check, user)).GetAsync(Today.AddDays(-1), Today.AddDays(1), CancellationToken.None);
        // Entrou R$ 100 líquidos menos R$ 4 de antecipação.
        Assert.Equal(96m, cash.Movements.Single(m => m.Kind == "recebimento").Amount);

        var dre = await new FinancialSummaryService(check, user).GetAsync(Today.AddDays(-1), Today.AddDays(1), CancellationToken.None);
        Assert.Equal(4m, dre.FinancialExpenses);
        // Receita líquida 300 - custo 120 - despesa financeira 4 (sem outras despesas): resultado 176.
        Assert.Equal(300m, dre.NetRevenue);
        Assert.Equal(120m, dre.CostOfGoodsSold);
        Assert.Equal(176m, dre.NetResult);
    }

    [DockerFact]
    public async Task Titulo_ja_antecipado_ou_ja_vencido_nao_pode_ser_antecipado()
    {
        var world = await SeedAsync();
        var sale = await SellInstallmentsAsync(world, 2);

        Guid first;
        Guid second;
        await using (var db = fixture.CreateContext())
        {
            var titles = await db.AccountsReceivables.Where(r => r.SaleId == sale.SaleId).OrderBy(r => r.InstallmentNumber).ToListAsync();
            first = titles[0].Id;
            second = titles[1].Id;
            titles[1].DueDate = Today.AddDays(-1);
            await db.SaveChangesAsync();
            await Cards(db, world).AnticipateAsync(first, CancellationToken.None);
        }

        await using var again = fixture.CreateContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Cards(again, world).AnticipateAsync(first, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Cards(again, world).AnticipateAsync(second, CancellationToken.None));
    }

    [DockerFact]
    public async Task Titulo_antigo_sem_modalidade_usa_a_taxa_padrao_de_2_5_por_cento()
    {
        var world = await SeedAsync();

        Guid titleId;
        await using (var db = fixture.CreateContext())
        {
            var legacy = new AccountsReceivable
            {
                CompanyId = world.User.CompanyId, ClientOrOrigin = "Adquirente", Type = "adquirente", Reference = "NFCe-111111",
                DueDate = Today.AddDays(20), GrossAmount = 200m, FeeRetention = 0m, NetAmount = 200m, Status = "a_vencer",
            };
            db.AccountsReceivables.Add(legacy);
            await db.SaveChangesAsync();
            titleId = legacy.Id;
            await Cards(db, world).AnticipateAsync(titleId, CancellationToken.None);
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(5m, (await check.AccountsReceivables.SingleAsync(r => r.Id == titleId)).AnticipationFee);
    }

    [DockerFact]
    public async Task Antecipar_titulo_de_outra_empresa_nao_faz_nada()
    {
        var world = await SeedAsync();
        var other = await SeedAsync();
        var sale = await SellInstallmentsAsync(other, 2);

        Guid otherTitle;
        await using (var db = fixture.CreateContext())
        {
            otherTitle = (await db.AccountsReceivables.FirstAsync(r => r.SaleId == sale.SaleId)).Id;
            await Cards(db, world).AnticipateAsync(otherTitle, CancellationToken.None);
        }

        await using var check = fixture.CreateContext();
        var title = await check.AccountsReceivables.SingleAsync(r => r.Id == otherTitle);
        Assert.Equal(("a_vencer", 0m), (title.Status, title.AnticipationFee));
    }

    [DockerFact]
    public async Task Taxa_de_antecipacao_configurada_e_validada()
    {
        var world = await SeedAsync();

        await using var db = fixture.CreateContext();
        var user = world.User;
        var fees = new FeeService(db, user, new AuditRecorder(db, user));

        var saved = await fees.SaveAsync(
            new SaveFeeRequest(world.Card.Id, null, null, null, null, null, null, null, null, AnticipationFeePercent: 3.25m), CancellationToken.None);
        Assert.Equal(3.25m, saved.AnticipationFeePercent);

        await Assert.ThrowsAsync<ArgumentException>(() => fees.SaveAsync(
            new SaveFeeRequest(world.Card.Id, null, null, null, null, null, null, null, null, AnticipationFeePercent: 100m), CancellationToken.None));
    }
}
