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
/// Fase 2: cancelamento com estorno, compra paga, edição travada, nota duplicada e concorrência.
/// Rodam contra PostgreSQL real (Testcontainers) e são ignorados quando o Docker não está disponível.
/// </summary>
public class PurchaseIntegrityTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
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

    private sealed record World(TestUser User, Company Company, Product Product);

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));

    /// <summary>Empresa nova com um produto zerado (custo 0), para o estoque vir só das compras do teste.</summary>
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
            Unit = "kg", Stock = 0, CostPrice = 0, SellingPrice = 100, AlertDays = 5,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return new World(new TestUser(company.Id, admin.Id), company, product);
    }

    private PurchaseService Purchases(GCStockDbContext db, ICurrentUserContext user) => new(db, user, new AuditRecorder(db, user));

    private static SavePurchaseRequest Request(
        Product product, decimal quantity = 10m, decimal unitCost = 20m, string mode = "a_prazo", string nfe = "1001",
        string supplier = "Frigorífico Bom Corte", string? cnpj = null) =>
        new(nfe, "1", Today, supplier, cnpj, null, null, null, null, null, null, mode == "a_prazo" ? "30 dias" : "À vista", mode,
            [new PurchaseItemDto(product.Name, product.Code, quantity, "kg", unitCost, quantity * unitCost, Today.AddDays(60), "L-" + nfe, true, null, product.Id)]);

    private async Task<PurchaseEntryDto> CreateAsync(World world, SavePurchaseRequest request)
    {
        await using var db = fixture.CreateContext();
        return await Purchases(db, world.User).SaveAsync(request, CancellationToken.None);
    }

    private async Task<decimal> StockAsync(World world)
    {
        await using var db = fixture.CreateContext();
        return (await db.Products.SingleAsync(p => p.Id == world.Product.Id)).Stock;
    }

    [DockerFact]
    public async Task Cancelar_compra_pendente_estorna_estoque_sem_apagar_historico()
    {
        var world = await SeedAsync();
        var purchase = await CreateAsync(world, Request(world.Product));
        Assert.Equal(10m, await StockAsync(world));

        await using (var db = fixture.CreateContext())
        {
            var cancelled = await Purchases(db, world.User).CancelAsync(purchase.Id, "lançada em duplicidade", CancellationToken.None);
            Assert.NotNull(cancelled);
            Assert.True(cancelled!.IsDeleted);
            Assert.Equal("nao_aplicavel", cancelled.RefundStatus);
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(0m, await StockAsync(world));

        var entry = await check.PurchaseEntries.SingleAsync(p => p.Id == purchase.Id);
        Assert.True(entry.IsDeleted);
        Assert.Equal("lançada em duplicidade", entry.CancelReason);
        Assert.Equal("Teste", entry.CancelledBy);
        Assert.NotNull(entry.CancelledAt);

        // Histórico preservado: a entrada original continua e o estorno é uma movimentação à parte.
        var movements = await check.StockMovements.Where(m => m.PurchaseEntryId == purchase.Id).ToListAsync();
        Assert.Contains(movements, m => m.Type == "entrada" && m.AmountKg == 10m);
        Assert.Contains(movements, m => m.Type == "estorno_compra" && m.AmountKg == -10m);

        // O lote continua registrado, mas zerado e marcado como cancelado.
        var batch = await check.StockBatches.SingleAsync(b => b.PurchaseEntryId == purchase.Id);
        Assert.Equal(0m, batch.AvailableKg);
        Assert.Equal("cancelado", batch.Status);

        // Título em aberto não representa dinheiro que saiu: some.
        Assert.False(await check.AccountsPayables.AnyAsync(p => p.PurchaseEntryId == purchase.Id));
    }

    [DockerFact]
    public async Task Cancelar_compra_duas_vezes_e_recusado()
    {
        var world = await SeedAsync();
        var purchase = await CreateAsync(world, Request(world.Product));

        await using var db = fixture.CreateContext();
        var service = Purchases(db, world.User);
        await service.CancelAsync(purchase.Id, "erro", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelAsync(purchase.Id, "erro", CancellationToken.None));
        Assert.Equal(0m, await StockAsync(world));
    }

    [DockerFact]
    public async Task Cancelar_exige_motivo()
    {
        var world = await SeedAsync();
        var purchase = await CreateAsync(world, Request(world.Product));

        await using var db = fixture.CreateContext();
        await Assert.ThrowsAsync<ArgumentException>(() => Purchases(db, world.User).CancelAsync(purchase.Id, "  ", CancellationToken.None));
        Assert.Equal(10m, await StockAsync(world));
    }

    [DockerFact]
    public async Task Compra_paga_cancelada_mantem_pagamento_e_fica_com_devolucao_pendente()
    {
        var world = await SeedAsync();
        var purchase = await CreateAsync(world, Request(world.Product, mode: "pix"));
        Assert.Equal("pago", purchase.PaymentStatus);

        await using (var db = fixture.CreateContext())
        {
            var cancelled = await Purchases(db, world.User).CancelAsync(purchase.Id, "produto errado", CancellationToken.None);
            Assert.Equal("pendente", cancelled!.RefundStatus);
        }

        await using var check = fixture.CreateContext();
        // O pagamento não é apagado.
        var payable = await check.AccountsPayables.SingleAsync(p => p.PurchaseEntryId == purchase.Id);
        Assert.Equal("pago", payable.Status);

        // O dinheiro continua saindo do caixa até o fornecedor devolver.
        var user = world.User;
        var cash = await new CashService(check, user, new AuditRecorder(check, user)).GetAsync(Today.AddDays(-1), Today.AddDays(1), CancellationToken.None);
        Assert.Contains(cash.Movements, m => m.Kind == "compra" && m.Amount == -200m);
        Assert.DoesNotContain(cash.Movements, m => m.Kind == "devolucao_compra");
    }

    [DockerFact]
    public async Task Devolucao_do_fornecedor_entra_no_caixa_e_nao_passa_do_valor_pago()
    {
        var world = await SeedAsync();
        var purchase = await CreateAsync(world, Request(world.Product, mode: "pix"));
        await using (var db = fixture.CreateContext())
        {
            await Purchases(db, world.User).CancelAsync(purchase.Id, "produto errado", CancellationToken.None);
        }

        await using (var db = fixture.CreateContext())
        {
            var partial = await Purchases(db, world.User)
                .RegisterRefundAsync(purchase.Id, new RegisterPurchaseRefundRequest(50m, Today, "estorno parcial"), CancellationToken.None);
            Assert.Equal("parcial", partial!.RefundStatus);
            Assert.Equal(50m, partial.RefundedAmount);
        }

        await using (var db = fixture.CreateContext())
        {
            // 50 já devolvidos + 200 passa dos 200 pagos.
            await Assert.ThrowsAsync<ArgumentException>(() => Purchases(db, world.User)
                .RegisterRefundAsync(purchase.Id, new RegisterPurchaseRefundRequest(200m, Today, null), CancellationToken.None));
        }

        await using (var db = fixture.CreateContext())
        {
            var full = await Purchases(db, world.User)
                .RegisterRefundAsync(purchase.Id, new RegisterPurchaseRefundRequest(150m, Today, null), CancellationToken.None);
            Assert.Equal("devolvido", full!.RefundStatus);
        }

        await using var check = fixture.CreateContext();
        var user = world.User;
        var cash = await new CashService(check, user, new AuditRecorder(check, user)).GetAsync(Today.AddDays(-1), Today.AddDays(1), CancellationToken.None);
        Assert.Equal(200m, cash.Movements.Where(m => m.Kind == "devolucao_compra").Sum(m => m.Amount));
        // Saída de 200 e devolução de 200: o saldo do período fecha em zero.
        Assert.Equal(0m, cash.ClosingBalance);
    }

    [DockerFact]
    public async Task Devolucao_so_vale_para_compra_paga_e_cancelada()
    {
        var world = await SeedAsync();
        var active = await CreateAsync(world, Request(world.Product, mode: "pix", nfe: "2001"));
        var pending = await CreateAsync(world, Request(world.Product, nfe: "2002"));
        await using (var db = fixture.CreateContext())
        {
            await Purchases(db, world.User).CancelAsync(pending.Id, "erro", CancellationToken.None);
        }

        await using var check = fixture.CreateContext();
        var service = Purchases(check, world.User);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RegisterRefundAsync(active.Id, new RegisterPurchaseRefundRequest(10m, Today, null), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RegisterRefundAsync(pending.Id, new RegisterPurchaseRefundRequest(10m, Today, null), CancellationToken.None));
    }

    [DockerFact]
    public async Task Cancelar_compra_com_parte_ja_vendida_e_recusado_e_nada_muda()
    {
        var world = await SeedAsync();
        var purchase = await CreateAsync(world, Request(world.Product));

        await using (var db = fixture.CreateContext())
        {
            var sale = new FinalizeSaleRequest(
                100m, "dinheiro", null, null, null, null, 0m, 100m,
                [new PosSaleItem("Picanha", 1m, "kg", 100m, ProductId: world.Product.Id)]);
            await new PosService(db, world.User, new AuditRecorder(db, world.User), new AllowAllPermissions())
                .FinalizeSaleAsync(sale, CancellationToken.None);
        }

        await using (var db = fixture.CreateContext())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Purchases(db, world.User).CancelAsync(purchase.Id, "erro", CancellationToken.None));
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(9m, await StockAsync(world));
        Assert.False((await check.PurchaseEntries.SingleAsync(p => p.Id == purchase.Id)).IsDeleted);
        Assert.False(await check.StockMovements.AnyAsync(m => m.PurchaseEntryId == purchase.Id && m.Type == "estorno_compra"));
    }

    [DockerFact]
    public async Task Compra_paga_nao_aceita_mudanca_de_itens_nem_de_valores()
    {
        var world = await SeedAsync();
        var purchase = await CreateAsync(world, Request(world.Product, mode: "pix"));

        await using var db = fixture.CreateContext();
        var service = Purchases(db, world.User);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateAsync(purchase.Id, Request(world.Product, quantity: 12m, mode: "pix"), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateAsync(purchase.Id, Request(world.Product, unitCost: 25m, mode: "pix"), CancellationToken.None));
        Assert.Equal(10m, await StockAsync(world));
    }

    [DockerFact]
    public async Task Compra_paga_aceita_correcao_de_fornecedor_e_nota()
    {
        var world = await SeedAsync();
        var purchase = await CreateAsync(world, Request(world.Product, mode: "pix", nfe: "3001"));

        await using (var db = fixture.CreateContext())
        {
            var corrected = Request(world.Product, mode: "pix", nfe: "3002", supplier: "Frigorífico Corrigido");
            var updated = await Purchases(db, world.User).UpdateAsync(purchase.Id, corrected, CancellationToken.None);
            Assert.Equal("3002", updated!.NfeNumber);
            Assert.Equal("Frigorífico Corrigido", updated.SupplierName);
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(10m, await StockAsync(world));
        // O histórico de estoque não foi mexido: continua só a entrada original.
        Assert.Single(await check.StockMovements.Where(m => m.PurchaseEntryId == purchase.Id).ToListAsync());
        Assert.Equal("3002", (await check.StockBatches.SingleAsync(b => b.PurchaseEntryId == purchase.Id)).FiscalDoc);
    }

    [DockerFact]
    public async Task Compra_pendente_editada_preserva_historico_e_ajusta_estoque()
    {
        var world = await SeedAsync();
        var purchase = await CreateAsync(world, Request(world.Product, quantity: 10m));

        await using (var db = fixture.CreateContext())
        {
            var updated = await Purchases(db, world.User)
                .UpdateAsync(purchase.Id, Request(world.Product, quantity: 15m), CancellationToken.None);
            Assert.Equal(300m, updated!.TotalAmount);
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(15m, await StockAsync(world));

        var movements = await check.StockMovements.Where(m => m.PurchaseEntryId == purchase.Id).ToListAsync();
        Assert.Equal(2, movements.Count(m => m.Type == "entrada"));
        Assert.Single(movements, m => m.Type == "estorno_compra");

        var batches = await check.StockBatches.Where(b => b.PurchaseEntryId == purchase.Id).ToListAsync();
        Assert.Equal(15m, batches.Where(b => b.Status != "cancelado").Sum(b => b.AvailableKg));
        Assert.Single(batches, b => b.Status == "cancelado");
    }

    [DockerFact]
    public async Task Edicao_nao_deixa_o_estoque_negativo_quando_parte_ja_foi_vendida()
    {
        var world = await SeedAsync();
        var purchase = await CreateAsync(world, Request(world.Product, quantity: 10m));

        await using (var db = fixture.CreateContext())
        {
            var sale = new FinalizeSaleRequest(
                500m, "dinheiro", null, null, null, null, 0m, 500m,
                [new PosSaleItem("Picanha", 5m, "kg", 100m, ProductId: world.Product.Id)]);
            await new PosService(db, world.User, new AuditRecorder(db, world.User), new AllowAllPermissions())
                .FinalizeSaleAsync(sale, CancellationToken.None);
        }

        await using (var db = fixture.CreateContext())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Purchases(db, world.User).UpdateAsync(purchase.Id, Request(world.Product, quantity: 3m), CancellationToken.None));
        }

        Assert.Equal(5m, await StockAsync(world));
    }

    [DockerFact]
    public async Task Nota_duplicada_do_mesmo_fornecedor_e_recusada()
    {
        var world = await SeedAsync();
        await CreateAsync(world, Request(world.Product, nfe: "4001", supplier: "Fornecedor A"));

        await using var db = fixture.CreateContext();
        await Assert.ThrowsAsync<ArgumentException>(
            () => Purchases(db, world.User).SaveAsync(Request(world.Product, nfe: "4001", supplier: "fornecedor a"), CancellationToken.None));
    }

    [DockerFact]
    public async Task Fornecedores_diferentes_podem_ter_a_mesma_numeracao_de_nota()
    {
        var world = await SeedAsync();
        await CreateAsync(world, Request(world.Product, nfe: "5001", supplier: "Fornecedor A", cnpj: "11.111.111/0001-11"));

        var other = await CreateAsync(world, Request(world.Product, nfe: "5001", supplier: "Fornecedor B", cnpj: "22.222.222/0001-22"));

        Assert.Equal("5001", other.NfeNumber);
    }

    [DockerFact]
    public async Task Nota_de_compra_cancelada_pode_ser_lancada_de_novo()
    {
        var world = await SeedAsync();
        var first = await CreateAsync(world, Request(world.Product, nfe: "6001"));
        await using (var db = fixture.CreateContext())
        {
            await Purchases(db, world.User).CancelAsync(first.Id, "lançada errada", CancellationToken.None);
        }

        var second = await CreateAsync(world, Request(world.Product, nfe: "6001"));

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(10m, await StockAsync(world));
    }

    [DockerFact]
    public async Task Lancamentos_simultaneos_geram_codigos_distintos_e_somam_todo_o_estoque()
    {
        var world = await SeedAsync();

        var tasks = Enumerable.Range(1, 6).Select(async i =>
        {
            await using var db = fixture.CreateContext();
            return await Purchases(db, world.User).SaveAsync(Request(world.Product, quantity: 2m, nfe: $"70{i:00}"), CancellationToken.None);
        });
        var created = await Task.WhenAll(tasks);

        Assert.Equal(6, created.Select(p => p.OrderCode).Distinct().Count());
        Assert.Equal(12m, await StockAsync(world));
    }

    [DockerFact]
    public async Task Compra_de_outra_empresa_nao_pode_ser_cancelada()
    {
        var world = await SeedAsync();
        var other = await SeedAsync();
        var purchase = await CreateAsync(other, Request(other.Product));

        await using var db = fixture.CreateContext();
        Assert.Null(await Purchases(db, world.User).CancelAsync(purchase.Id, "invasão", CancellationToken.None));

        await using var check = fixture.CreateContext();
        Assert.False((await check.PurchaseEntries.SingleAsync(p => p.Id == purchase.Id)).IsDeleted);
    }
}
