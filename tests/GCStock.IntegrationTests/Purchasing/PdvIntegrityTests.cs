using GCStock.Application.Abstractions;
using GCStock.Application.Pdv;
using GCStock.Domain.Entities;
using GCStock.Infra.Database;
using GCStock.Infra.Services;
using GCStock.IntegrationTests.TenantIsolation;
using Microsoft.EntityFrameworkCore;

namespace GCStock.IntegrationTests.Purchasing;

/// <summary>
/// Fase 6A: o servidor calcula total, taxa, líquido e desconto; número de venda único; parcelas que fecham o total;
/// recebíveis ligados à venda exata. Rodam contra PostgreSQL real (Testcontainers); ignorados sem Docker.
/// </summary>
public class PdvIntegrityTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
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

    private sealed record World(TestUser User, Product Product, PaymentFeeSetting Card, PaymentFeeSetting Installment, Client Client);

    /// <summary>Empresa com um produto (R$ 100/kg, estoque 100), um cartão à vista (3,5% + R$ 0,40), um parcelado (2%) e um cliente.</summary>
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
            Unit = "kg", Stock = 100, CostPrice = 40, SellingPrice = 100, AlertDays = 5,
        };
        var card = new PaymentFeeSetting { CompanyId = company.Id, Method = "Cartão de Crédito à vista", FeePercent = 3.5m, FixedFee = 0.40m, SortOrder = 1 };
        var installment = new PaymentFeeSetting { CompanyId = company.Id, Method = "Cartão de Crédito Parcelado", FeePercent = 2m, SortOrder = 2 };
        var client = new Client { CompanyId = company.Id, Name = "Cliente Fiado" };
        db.AddRange(product, card, installment, client);
        await db.SaveChangesAsync();
        return new World(new TestUser(company.Id, admin.Id), product, card, installment, client);
    }

    private PosService Pos(GCStockDbContext db, World world) =>
        new(db, world.User, new AuditRecorder(db, world.User), new AllowAllPermissions());

    private static List<PosSaleItem> Items(Product product, decimal quantity) =>
        [new PosSaleItem("Picanha", quantity, "kg", 100m, ProductId: product.Id)];

    [DockerFact]
    public async Task Servidor_calcula_taxa_total_e_liquido_pela_forma_de_pagamento_cadastrada()
    {
        var world = await SeedAsync();

        FinalizeSaleResult result;
        await using (var db = fixture.CreateContext())
        {
            // 1 kg a R$ 100 no cartão a vista: taxa 3,50 + 0,40 = 3,90, repassada: cliente paga 103,90 e o lojista recebe 100.
            var request = new FinalizeSaleRequest(
                103.90m, "credito_vista", null, null, null, null, 0m, 0m, Items(world.Product, 1m), PaymentOptionId: world.Card.Id);
            result = await Pos(db, world).FinalizeSaleAsync(request, CancellationToken.None);
        }

        Assert.Equal((103.90m, 100m, 3.90m), (result.Total, result.NetAmount, result.FeeAmount));

        await using var check = fixture.CreateContext();
        var sale = await check.SaleRecords.SingleAsync(s => s.SaleId == result.SaleId);
        Assert.Equal(("credito_vista", 103.90m, 100m, 3.90m), (sale.PaymentMode, sale.Total, sale.NetAmount, sale.FeeAmount));
    }

    [DockerFact]
    public async Task Valores_enviados_pela_tela_nao_sao_confiados()
    {
        var world = await SeedAsync();

        await using (var db = fixture.CreateContext())
        {
            // A tela diz R$ 50 por 1 kg de picanha a R$ 100 com taxa: o servidor recusa em vez de cobrar errado.
            var tampered = new FinalizeSaleRequest(
                50m, "credito_vista", null, null, null, null, 0m, 50m, Items(world.Product, 1m), PaymentOptionId: world.Card.Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Pos(db, world).FinalizeSaleAsync(tampered, CancellationToken.None));
        }

        await using var check = fixture.CreateContext();
        Assert.False(await check.SaleRecords.AnyAsync(s => s.CompanyId == world.User.CompanyId));
        Assert.Equal(100m, (await check.Products.SingleAsync(p => p.Id == world.Product.Id)).Stock);
    }

    [DockerFact]
    public async Task Desconto_e_gravado_e_desconto_maior_que_o_total_e_recusado()
    {
        var world = await SeedAsync();

        await using (var db = fixture.CreateContext())
        {
            var request = new FinalizeSaleRequest(
                180m, "dinheiro", null, null, null, null, 0m, 180m, Items(world.Product, 2m), Discount: 20m);
            var result = await Pos(db, world).FinalizeSaleAsync(request, CancellationToken.None);
            Assert.Equal(20m, result.DiscountAmount);
            Assert.Equal(180m, result.Total);
        }

        await using (var db = fixture.CreateContext())
        {
            var tooMuch = new FinalizeSaleRequest(
                0m, "dinheiro", null, null, null, null, 0m, 0m, Items(world.Product, 1m), Discount: 150m);
            await Assert.ThrowsAsync<ArgumentException>(() => Pos(db, world).FinalizeSaleAsync(tooMuch, CancellationToken.None));
        }
    }

    [DockerFact]
    public async Task Numero_da_venda_e_sequencial_e_unico_mesmo_com_vendas_simultaneas()
    {
        var world = await SeedAsync();

        var tasks = Enumerable.Range(1, 6).Select(async _ =>
        {
            await using var db = fixture.CreateContext();
            var request = new FinalizeSaleRequest(100m, "dinheiro", null, null, null, null, 0m, 100m, Items(world.Product, 1m));
            return await Pos(db, world).FinalizeSaleAsync(request, CancellationToken.None);
        });
        var results = await Task.WhenAll(tasks);

        var numbers = results.Select(r => r.ReceiptNumber).ToList();
        Assert.Equal(6, numbers.Distinct().Count());
        Assert.All(numbers, n => Assert.Matches(@"^VEN-\d{4}-\d{6}$", n));
        Assert.Equal(Enumerable.Range(1, 6).Select(i => $"{i:D6}"), numbers.Select(n => n[^6..]).Order());
    }

    [DockerFact]
    public async Task Cartao_parcelado_gera_uma_parcela_por_vencimento_e_fecha_total_taxa_e_liquido()
    {
        var world = await SeedAsync();

        FinalizeSaleResult result;
        await using (var db = fixture.CreateContext())
        {
            // R$ 100,01 em 3x com taxa de 2% repassada: taxa 2,00, total 102,01.
            var items = new List<PosSaleItem> { new("Picanha", 1m, "kg", 100.01m, ProductId: world.Product.Id) };
            var request = new FinalizeSaleRequest(
                102.01m, "parcelado", null, null, null, null, 0m, 0m, items, PaymentOptionId: world.Installment.Id, Installments: 3);
            result = await Pos(db, world).FinalizeSaleAsync(request, CancellationToken.None);
        }

        await using var check = fixture.CreateContext();
        var parts = await check.AccountsReceivables.Where(r => r.SaleId == result.SaleId).OrderBy(r => r.InstallmentNumber).ToListAsync();
        Assert.Equal(3, parts.Count);
        Assert.Equal(new[] { 1, 2, 3 }, parts.Select(p => p.InstallmentNumber));
        Assert.All(parts, p => Assert.Equal(3, p.InstallmentCount));
        Assert.Equal(result.Total, parts.Sum(p => p.GrossAmount));
        Assert.Equal(result.FeeAmount, parts.Sum(p => p.FeeRetention));
        Assert.Equal(result.NetAmount, parts.Sum(p => p.NetAmount));
        Assert.Equal(3, parts.Select(p => p.DueDate).Distinct().Count());
    }

    [DockerFact]
    public async Task Venda_a_prazo_em_parcelas_vence_de_mes_em_mes_e_soma_o_total()
    {
        var world = await SeedAsync();
        var firstDue = DateOnly.FromDateTime(DateTime.Today.AddDays(30));

        FinalizeSaleResult result;
        await using (var db = fixture.CreateContext())
        {
            var request = new FinalizeSaleRequest(
                200m, "a_prazo", null, world.Client.Id, world.Client.Name, firstDue, 0m, 200m, Items(world.Product, 2m), Installments: 4);
            result = await Pos(db, world).FinalizeSaleAsync(request, CancellationToken.None);
        }

        await using var check = fixture.CreateContext();
        var parts = await check.AccountsReceivables.Where(r => r.SaleId == result.SaleId).OrderBy(r => r.InstallmentNumber).ToListAsync();
        Assert.Equal(4, parts.Count);
        Assert.Equal(200m, parts.Sum(p => p.GrossAmount));
        Assert.Equal(firstDue.AddMonths(3), parts[3].DueDate);
        Assert.Equal(200m, (await check.Clients.SingleAsync(c => c.Id == world.Client.Id)).TotalSpent);
    }

    [DockerFact]
    public async Task Parcelas_fora_do_limite_sao_recusadas_e_pagamento_sem_parcelamento_ignora_o_numero()
    {
        var world = await SeedAsync();

        await using (var db = fixture.CreateContext())
        {
            var invalid = new FinalizeSaleRequest(
                100m, "a_prazo", null, world.Client.Id, world.Client.Name, DateOnly.FromDateTime(DateTime.Today.AddDays(30)), 0m, 100m,
                Items(world.Product, 1m), Installments: 13);
            await Assert.ThrowsAsync<ArgumentException>(() => Pos(db, world).FinalizeSaleAsync(invalid, CancellationToken.None));
        }

        await using (var db = fixture.CreateContext())
        {
            var pix = new FinalizeSaleRequest(100m, "pix", null, null, null, null, 0m, 100m, Items(world.Product, 1m), Installments: 5);
            var result = await Pos(db, world).FinalizeSaleAsync(pix, CancellationToken.None);
            Assert.Equal(1, result.Installments);
        }
    }

    [DockerFact]
    public async Task Cancelar_a_venda_remove_todas_as_parcelas_dela_e_so_delas()
    {
        var world = await SeedAsync();
        var firstDue = DateOnly.FromDateTime(DateTime.Today.AddDays(30));

        FinalizeSaleResult cancelled;
        FinalizeSaleResult other;
        await using (var db = fixture.CreateContext())
        {
            cancelled = await Pos(db, world).FinalizeSaleAsync(new FinalizeSaleRequest(
                200m, "a_prazo", null, world.Client.Id, world.Client.Name, firstDue, 0m, 200m, Items(world.Product, 2m), Installments: 2), CancellationToken.None);
        }
        await using (var db = fixture.CreateContext())
        {
            other = await Pos(db, world).FinalizeSaleAsync(new FinalizeSaleRequest(
                100m, "a_prazo", null, world.Client.Id, world.Client.Name, firstDue, 0m, 100m, Items(world.Product, 1m)), CancellationToken.None);
        }

        await using (var db = fixture.CreateContext())
        {
            var user = world.User;
            Assert.NotNull(await new SaleService(db, user, new AuditRecorder(db, user)).CancelAsync(cancelled.SaleId, "teste", CancellationToken.None));
        }

        await using var check = fixture.CreateContext();
        Assert.False(await check.AccountsReceivables.AnyAsync(r => r.SaleId == cancelled.SaleId));
        Assert.Single(await check.AccountsReceivables.Where(r => r.SaleId == other.SaleId).ToListAsync());
    }

    [DockerFact]
    public async Task Cancelar_e_recusado_se_alguma_parcela_ja_foi_recebida()
    {
        var world = await SeedAsync();

        FinalizeSaleResult result;
        await using (var db = fixture.CreateContext())
        {
            result = await Pos(db, world).FinalizeSaleAsync(new FinalizeSaleRequest(
                200m, "a_prazo", null, world.Client.Id, world.Client.Name, DateOnly.FromDateTime(DateTime.Today.AddDays(30)), 0m, 200m,
                Items(world.Product, 2m), Installments: 2), CancellationToken.None);
        }

        await using (var db = fixture.CreateContext())
        {
            var first = await db.AccountsReceivables.Where(r => r.SaleId == result.SaleId).OrderBy(r => r.InstallmentNumber).FirstAsync();
            first.Status = "recebido";
            first.ReceivedDate = DateOnly.FromDateTime(DateTime.Today);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            var user = world.User;
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => new SaleService(db, user, new AuditRecorder(db, user)).CancelAsync(result.SaleId, "teste", CancellationToken.None));
        }

        await using var check = fixture.CreateContext();
        Assert.Equal("concluida", (await check.SaleRecords.SingleAsync(s => s.SaleId == result.SaleId)).Status);
        Assert.Equal(98m, (await check.Products.SingleAsync(p => p.Id == world.Product.Id)).Stock);
    }
}
