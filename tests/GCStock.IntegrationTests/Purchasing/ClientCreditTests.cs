using GCStock.Application.Abstractions;
using GCStock.Application.Access;
using GCStock.Application.Pdv;
using GCStock.Application.Sales;
using GCStock.Domain.Entities;
using GCStock.Infra.Database;
using GCStock.Infra.Services;
using GCStock.IntegrationTests.TenantIsolation;
using Microsoft.EntityFrameworkCore;

namespace GCStock.IntegrationTests.Purchasing;

/// <summary>
/// Fase 8: indicadores do cliente calculados das vendas (comprado, pago, em aberto, ticket, frequência), histórico completo
/// e limite de crédito aplicado no servidor, com exceção por permissão. Rodam contra PostgreSQL real; ignorados sem Docker.
/// </summary>
public class ClientCreditTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
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

    /// <summary>Permissões: tudo liberado, exceto (opcionalmente) a de exceder o limite de crédito.</summary>
    private sealed class Permissions(bool canExceedCredit) : IPermissionChecker
    {
        public Task<bool> HasAsync(string permission, CancellationToken ct) =>
            Task.FromResult(permission != Access.SalesExceedCredit || canExceedCredit);
        public Task<List<string>> GetCurrentAsync(CancellationToken ct) => Task.FromResult(new List<string>());
    }

    private static class Access
    {
        public const string SalesExceedCredit = GCStock.Application.Access.Permissions.SalesExceedCredit;
    }

    private sealed record World(TestUser User, Product Product, Client Client);

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));

    private async Task<World> SeedAsync(decimal creditLimit)
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
        var client = new Client { CompanyId = company.Id, Name = "Cliente Fiado", CreditLimit = creditLimit };
        db.AddRange(product, client);
        await db.SaveChangesAsync();
        return new World(new TestUser(company.Id, admin.Id), product, client);
    }

    private PosService Pos(GCStockDbContext db, World world, bool canExceedCredit = false) =>
        new(db, world.User, new AuditRecorder(db, world.User), new Permissions(canExceedCredit));

    private ClientService Clients(GCStockDbContext db, World world) => new(db, world.User, new AuditRecorder(db, world.User));

    private static FinalizeSaleRequest Sale(World world, decimal kg, string mode) =>
        new(kg * 100m, mode, null, world.Client.Id, world.Client.Name,
            mode == "a_prazo" ? DateOnly.FromDateTime(DateTime.Today.AddDays(30)) : null, 0m, kg * 100m,
            [new PosSaleItem("Picanha", kg, "kg", 100m, ProductId: world.Product.Id)]);

    private async Task<FinalizeSaleResult> SellAsync(World world, decimal kg, string mode, bool canExceedCredit = false)
    {
        await using var db = fixture.CreateContext();
        return await Pos(db, world, canExceedCredit).FinalizeSaleAsync(Sale(world, kg, mode), CancellationToken.None);
    }

    private async Task<Application.Clients.ClientDto> ClientDtoAsync(World world)
    {
        await using var db = fixture.CreateContext();
        return (await Clients(db, world).GetAllAsync(CancellationToken.None)).Single(c => c.Id == world.Client.Id);
    }

    [DockerFact]
    public async Task Total_comprado_pago_e_em_aberto_somam_todas_as_formas_de_pagamento()
    {
        var world = await SeedAsync(creditLimit: 0m);
        await SellAsync(world, 2m, "dinheiro");
        await SellAsync(world, 3m, "a_prazo");

        var dto = await ClientDtoAsync(world);

        Assert.Equal(500m, dto.TotalSpent);
        Assert.Equal(300m, dto.OpenBalance);
        Assert.Equal(200m, dto.TotalPaid);
        Assert.Equal(2, dto.PurchaseCount);
        Assert.Equal(250m, dto.AverageTicket);
        Assert.Equal(Today, dto.LastPurchase);
        Assert.Equal(Today, dto.FirstPurchase);
    }

    [DockerFact]
    public async Task Receber_o_titulo_zera_o_saldo_em_aberto_e_conta_como_pago()
    {
        var world = await SeedAsync(creditLimit: 0m);
        var sale = await SellAsync(world, 3m, "a_prazo");

        await using (var db = fixture.CreateContext())
        {
            var title = await db.AccountsReceivables.SingleAsync(r => r.SaleId == sale.SaleId);
            title.Status = "recebido";
            title.ReceivedDate = Today;
            await db.SaveChangesAsync();
        }

        var dto = await ClientDtoAsync(world);
        Assert.Equal((300m, 0m, 300m), (dto.TotalSpent, dto.OpenBalance, dto.TotalPaid));
    }

    [DockerFact]
    public async Task Venda_cancelada_nao_conta_e_devolucao_reduz_o_total_comprado()
    {
        var world = await SeedAsync(creditLimit: 0m);
        var cancelled = await SellAsync(world, 4m, "dinheiro");
        var kept = await SellAsync(world, 3m, "dinheiro");

        await using (var db = fixture.CreateContext())
        {
            var sales = new SaleService(db, world.User, new AuditRecorder(db, world.User));
            await sales.CancelAsync(cancelled.SaleId, "teste", CancellationToken.None);
        }
        await using (var db = fixture.CreateContext())
        {
            var itemId = await db.SaleItems.Where(i => i.SaleRecord!.SaleId == kept.SaleId).Select(i => i.Id).SingleAsync();
            var sales = new SaleService(db, world.User, new AuditRecorder(db, world.User));
            await sales.ReturnItemsAsync(kept.SaleId, new ReturnSaleRequest("cliente devolveu", [new ReturnSaleLine(itemId, 1m, true)]), CancellationToken.None);
        }

        var dto = await ClientDtoAsync(world);
        Assert.Equal((200m, 1), (dto.TotalSpent, dto.PurchaseCount));
    }

    [DockerFact]
    public async Task Historico_lista_todas_as_compras_do_cliente_com_pagamento_e_o_que_mais_compra()
    {
        var world = await SeedAsync(creditLimit: 0m);
        await SellAsync(world, 2m, "pix");
        await SellAsync(world, 3m, "a_prazo");

        await using var db = fixture.CreateContext();
        var history = await Clients(db, world).GetHistoryAsync(world.Client.Id, CancellationToken.None);

        Assert.NotNull(history);
        Assert.Equal(2, history!.Purchases.Count);
        Assert.Equal(new[] { "a_prazo", "pix" }, history.Purchases.Select(p => p.PaymentMode).Order());
        Assert.Equal(300m, history.Purchases.Single(p => p.PaymentMode == "a_prazo").OpenAmount);
        Assert.Equal(0m, history.Purchases.Single(p => p.PaymentMode == "pix").OpenAmount);
        var top = Assert.Single(history.TopProducts);
        Assert.Equal(("Picanha", 5m), (top.Name, top.Qty));
        Assert.Empty(history.TopKits);
    }

    [DockerFact]
    public async Task Historico_de_cliente_de_outra_empresa_nao_e_encontrado()
    {
        var world = await SeedAsync(0m);
        var other = await SeedAsync(0m);

        await using var db = fixture.CreateContext();
        Assert.Null(await Clients(db, world).GetHistoryAsync(other.Client.Id, CancellationToken.None));
    }

    [DockerFact]
    public async Task Venda_a_prazo_acima_do_limite_e_bloqueada_no_servidor()
    {
        var world = await SeedAsync(creditLimit: 500m);
        await SellAsync(world, 3m, "a_prazo");

        await using (var db = fixture.CreateContext())
        {
            // Em aberto 300 + nova venda 300 = 600 > 500.
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Pos(db, world).FinalizeSaleAsync(Sale(world, 3m, "a_prazo"), CancellationToken.None));
            Assert.Contains("Limite de crédito excedido", ex.Message);
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(1, await check.SaleRecords.CountAsync(s => s.CompanyId == world.User.CompanyId));
        Assert.Equal(97m, (await check.Products.SingleAsync(p => p.Id == world.Product.Id)).Stock);

        var dto = await ClientDtoAsync(world);
        Assert.Equal((300m, 200m), (dto.OpenBalance, dto.CreditAvailable));
        Assert.False(dto.CreditExceeded);
    }

    [DockerFact]
    public async Task Quem_tem_a_permissao_pode_exceder_o_limite_e_a_excecao_fica_registrada()
    {
        var world = await SeedAsync(creditLimit: 500m);
        await SellAsync(world, 3m, "a_prazo");

        await SellAsync(world, 3m, "a_prazo", canExceedCredit: true);

        var dto = await ClientDtoAsync(world);
        Assert.Equal(600m, dto.OpenBalance);
        Assert.True(dto.CreditExceeded);

        await using var check = fixture.CreateContext();
        Assert.Contains(
            await check.AuditLogEntries.Where(a => a.CompanyId == world.User.CompanyId && a.Action == "vendeu").ToListAsync(),
            a => a.Details != null && a.Details.Contains("limite de crédito excedido"));
    }

    [DockerFact]
    public async Task Limite_zero_significa_sem_limite_definido()
    {
        var world = await SeedAsync(creditLimit: 0m);

        await SellAsync(world, 5m, "a_prazo");
        await SellAsync(world, 5m, "a_prazo");

        Assert.Equal(1000m, (await ClientDtoAsync(world)).OpenBalance);
    }

    [DockerFact]
    public async Task Venda_a_prazo_sem_cliente_e_recusada()
    {
        var world = await SeedAsync(0m);

        await using var db = fixture.CreateContext();
        var request = Sale(world, 1m, "a_prazo") with { ClientId = null, ClientName = null };
        await Assert.ThrowsAsync<ArgumentException>(() => Pos(db, world).FinalizeSaleAsync(request, CancellationToken.None));
    }

    [DockerFact]
    public async Task Vendas_a_prazo_simultaneas_nao_estouram_o_limite_juntas()
    {
        var world = await SeedAsync(creditLimit: 500m);

        var attempts = Enumerable.Range(1, 2).Select(async _ =>
        {
            await using var db = fixture.CreateContext();
            try
            {
                await Pos(db, world).FinalizeSaleAsync(Sale(world, 3m, "a_prazo"), CancellationToken.None);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        });
        var outcomes = await Task.WhenAll(attempts);

        // Cada venda é de R$ 300 e o limite é R$ 500: só uma pode passar.
        Assert.Equal(1, outcomes.Count(o => o));
        Assert.Equal(300m, (await ClientDtoAsync(world)).OpenBalance);
    }
}
