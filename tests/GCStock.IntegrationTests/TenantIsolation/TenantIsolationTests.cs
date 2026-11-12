using GCStock.Application.Abstractions;
using GCStock.Application.Catalog;
using GCStock.Application.Pdv;
using GCStock.Application.Purchasing;
using GCStock.Domain.Entities;
using GCStock.Infra.Database;
using GCStock.Infra.Services;
using Microsoft.EntityFrameworkCore;

namespace GCStock.IntegrationTests.TenantIsolation;

/// <summary>
/// Isolamento entre empresas: a empresa A nunca pode ler nem alterar registros da empresa B informando o ID dele.
/// Rodam contra PostgreSQL real (Testcontainers) e são ignorados quando o Docker não está disponível.
/// </summary>
public class TenantIsolationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private sealed class TestUser(Guid companyId, Guid administratorId, bool isOwner = true) : ICurrentUserContext
    {
        public bool IsAuthenticated => true;
        public Guid UserId { get; } = administratorId;
        public string UserName => "Teste";
        public string UserEmail => "teste@gcstock.test";
        public string Role => "admin";
        public Guid CompanyId { get; } = companyId;
        public Guid? AdministratorId { get; } = administratorId;
        public Guid? EmployeeId => null;
        public bool IsOwner { get; } = isOwner;
    }

    private sealed class AllowAllPermissions : IPermissionChecker
    {
        public Task<bool> HasAsync(string permission, CancellationToken ct) => Task.FromResult(true);
        public Task<List<string>> GetCurrentAsync(CancellationToken ct) => Task.FromResult(new List<string>());
    }

    /// <summary>Duas empresas independentes (donos diferentes), cada uma com cliente, produto, funcionário e uma segunda empresa do mesmo dono.</summary>
    private sealed record World(
        Administrator AdminA, Company CompanyA, Company CompanyA2, Employee EmployeeA, Client ClientA, Product ProductA,
        Administrator AdminB, Company CompanyB, Company CompanyB2, Employee EmployeeB, Client ClientB, Product ProductB)
    {
        public TestUser UserA => new(CompanyA.Id, AdminA.Id);
        public TestUser UserB => new(CompanyB.Id, AdminB.Id);
    }

    private async Task<World> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Administrator NewAdmin(string tag) => new() { Name = $"Dono {tag}", Email = $"{tag}-{suffix}@gcstock.test", PasswordHash = "x" };
        Company NewCompany(Administrator owner, string name) => new()
        {
            CorporateName = name, TradeName = name, Document = "00.000.000/0001-00", OwnerId = owner.Id,
        };
        Employee NewEmployee(Company company, string tag) => new()
        {
            CompanyId = company.Id, Name = $"Func {tag}", Email = $"func-{tag}-{suffix}@gcstock.test", PasswordHash = "x",
        };
        Client NewClient(Company company, string tag) => new() { CompanyId = company.Id, Name = $"Cliente {tag}" };
        Product NewProduct(Company company, string tag) => new()
        {
            CompanyId = company.Id, Name = $"Produto {tag}", Sku = $"SKU-{tag}-{suffix}", Code = $"C-{tag}-{suffix}",
            Unit = "kg", Stock = 10, CostPrice = 5, SellingPrice = 10,
        };

        var adminA = NewAdmin("a");
        var adminB = NewAdmin("b");
        var companyA = NewCompany(adminA, $"Empresa A {suffix}");
        var companyA2 = NewCompany(adminA, $"Empresa A2 {suffix}");
        var companyB = NewCompany(adminB, $"Empresa B {suffix}");
        var companyB2 = NewCompany(adminB, $"Empresa B2 {suffix}");
        var employeeA = NewEmployee(companyA, "a");
        var employeeB = NewEmployee(companyB, "b");
        var clientA = NewClient(companyA, "a");
        var clientB = NewClient(companyB, "b");
        var productA = NewProduct(companyA, "a");
        var productB = NewProduct(companyB, "b");

        db.Administrators.AddRange(adminA, adminB);
        await db.SaveChangesAsync();
        db.Companies.AddRange(companyA, companyA2, companyB, companyB2);
        await db.SaveChangesAsync();
        db.Employees.AddRange(employeeA, employeeB);
        db.Clients.AddRange(clientA, clientB);
        db.Products.AddRange(productA, productB);
        await db.SaveChangesAsync();

        return new World(adminA, companyA, companyA2, employeeA, clientA, productA, adminB, companyB, companyB2, employeeB, clientB, productB);
    }

    private PosService Pos(GCStockDbContext db, ICurrentUserContext user) =>
        new(db, user, new AuditRecorder(db, user), new AllowAllPermissions());

    private static FinalizeSaleRequest CashSale(Guid? productId, Guid? clientId = null, string paymentMode = "dinheiro") =>
        new(10m, paymentMode, null, clientId, null, null, 0m, 10m,
            [new PosSaleItem("Produto", 1m, "kg", 10m, ProductId: productId)]);

    [DockerFact]
    public async Task Venda_com_cliente_de_outra_empresa_e_recusada_e_nao_altera_o_cliente()
    {
        var world = await SeedAsync();

        await using (var db = fixture.CreateContext())
        {
            var request = CashSale(world.ProductA.Id, world.ClientB.Id, paymentMode: "a_prazo") with { DueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)) };
            await Assert.ThrowsAsync<ArgumentException>(() => Pos(db, world.UserA).FinalizeSaleAsync(request, CancellationToken.None));
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(0m, (await check.Clients.SingleAsync(c => c.Id == world.ClientB.Id)).TotalSpent);
        Assert.Equal(10m, (await check.Products.SingleAsync(p => p.Id == world.ProductA.Id)).Stock);
        Assert.False(await check.SaleRecords.AnyAsync(s => s.CompanyId == world.CompanyA.Id));
    }

    [DockerFact]
    public async Task Venda_com_produto_de_outra_empresa_e_recusada()
    {
        var world = await SeedAsync();

        await using (var db = fixture.CreateContext())
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => Pos(db, world.UserA).FinalizeSaleAsync(CashSale(world.ProductB.Id), CancellationToken.None));
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(10m, (await check.Products.SingleAsync(p => p.Id == world.ProductB.Id)).Stock);
        Assert.False(await check.SaleRecords.AnyAsync(s => s.CompanyId == world.CompanyA.Id));
    }

    [DockerFact]
    public async Task Venda_com_cliente_e_produto_da_propria_empresa_funciona()
    {
        var world = await SeedAsync();

        await using (var db = fixture.CreateContext())
        {
            var request = CashSale(world.ProductA.Id, world.ClientA.Id, paymentMode: "a_prazo") with { DueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)) };
            await Pos(db, world.UserA).FinalizeSaleAsync(request, CancellationToken.None);
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(10m, (await check.Clients.SingleAsync(c => c.Id == world.ClientA.Id)).TotalSpent);
        Assert.Equal(9m, (await check.Products.SingleAsync(p => p.Id == world.ProductA.Id)).Stock);
    }

    [DockerFact]
    public async Task Empresa_A_nao_cancela_venda_da_empresa_B()
    {
        var world = await SeedAsync();

        Guid saleId;
        await using (var db = fixture.CreateContext())
        {
            saleId = (await Pos(db, world.UserB).FinalizeSaleAsync(CashSale(world.ProductB.Id), CancellationToken.None)).SaleId;
        }

        await using (var db = fixture.CreateContext())
        {
            var user = world.UserA;
            var result = await new SaleService(db, user, new AuditRecorder(db, user)).CancelAsync(saleId, "teste", CancellationToken.None);
            Assert.Null(result);
        }

        await using var check = fixture.CreateContext();
        Assert.NotEqual("cancelada", (await check.SaleRecords.SingleAsync(s => s.Id == saleId)).Status);
        Assert.Equal(9m, (await check.Products.SingleAsync(p => p.Id == world.ProductB.Id)).Stock);
    }

    [DockerFact]
    public async Task Empresa_A_nao_exclui_cliente_da_empresa_B()
    {
        var world = await SeedAsync();

        await using (var db = fixture.CreateContext())
        {
            var user = world.UserA;
            Assert.False(await new ClientService(db, user, new AuditRecorder(db, user)).DeleteAsync(world.ClientB.Id, CancellationToken.None));
        }

        await using var check = fixture.CreateContext();
        Assert.True(await check.Clients.AnyAsync(c => c.Id == world.ClientB.Id));
    }

    [DockerFact]
    public async Task Dono_nao_transfere_funcionario_de_outra_empresa()
    {
        var world = await SeedAsync();

        await using (var db = fixture.CreateContext())
        {
            // A tenta mover o funcionário de B entre as empresas de B.
            var user = world.UserA;
            var service = new EmployeeService(db, user, new FakeHasher(), new AuditRecorder(db, user));
            Assert.Null(await service.TransferAsync(world.EmployeeB.Id, world.CompanyB2.Id, CancellationToken.None));
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(world.CompanyB.Id, (await check.Employees.SingleAsync(e => e.Id == world.EmployeeB.Id)).CompanyId);
    }

    [DockerFact]
    public async Task Dono_transfere_funcionario_da_propria_empresa_para_outra_empresa_sua()
    {
        var world = await SeedAsync();

        await using (var db = fixture.CreateContext())
        {
            var user = world.UserA;
            var service = new EmployeeService(db, user, new FakeHasher(), new AuditRecorder(db, user));
            Assert.NotNull(await service.TransferAsync(world.EmployeeA.Id, world.CompanyA2.Id, CancellationToken.None));
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(world.CompanyA2.Id, (await check.Employees.SingleAsync(e => e.Id == world.EmployeeA.Id)).CompanyId);
    }

    [DockerFact]
    public async Task Dono_nao_transfere_funcionario_para_empresa_de_outro_dono()
    {
        var world = await SeedAsync();

        await using (var db = fixture.CreateContext())
        {
            var user = world.UserA;
            var service = new EmployeeService(db, user, new FakeHasher(), new AuditRecorder(db, user));
            Assert.Null(await service.TransferAsync(world.EmployeeA.Id, world.CompanyB.Id, CancellationToken.None));
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(world.CompanyA.Id, (await check.Employees.SingleAsync(e => e.Id == world.EmployeeA.Id)).CompanyId);
    }

    [DockerFact]
    public async Task Kit_nao_aceita_componente_de_produto_de_outra_empresa()
    {
        var world = await SeedAsync();

        await using var db = fixture.CreateContext();
        var user = world.UserA;
        var component = new KitComponentDto(world.ProductB.Id, "Produto B", "C", "", 1m, "kg", 5m, 5m, 10m);
        var request = new SaveKitRequest(null, "Kit invasor", null, null, null, 100m, null, null, null, null, null, [component], null);

        await Assert.ThrowsAsync<ArgumentException>(
            () => new KitService(db, user, new AuditRecorder(db, user)).SaveAsync(request, CancellationToken.None));
        Assert.False(await db.Kits.AnyAsync(k => k.CompanyId == world.CompanyA.Id));
    }

    [DockerFact]
    public async Task Compra_nao_aceita_item_de_produto_de_outra_empresa()
    {
        var world = await SeedAsync();

        await using (var db = fixture.CreateContext())
        {
            var user = world.UserA;
            var item = new PurchaseItemDto("Produto B", "C", 5m, "kg", 4m, 20m, null, "L1", false, null, world.ProductB.Id);
            var request = new SavePurchaseRequest(
                "123", "1", DateOnly.FromDateTime(DateTime.Today), "Fornecedor", null, null, null,
                null, null, null, null, null, "pix", [item]);

            await Assert.ThrowsAsync<ArgumentException>(
                () => new PurchaseService(db, user, new AuditRecorder(db, user)).SaveAsync(request, CancellationToken.None));
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(10m, (await check.Products.SingleAsync(p => p.Id == world.ProductB.Id)).Stock);
        Assert.False(await check.PurchaseEntries.AnyAsync(p => p.CompanyId == world.CompanyA.Id));
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public string Hash(string password) => "hash:" + password;
        public bool Verify(string password, string hash) => hash == "hash:" + password;
    }
}
