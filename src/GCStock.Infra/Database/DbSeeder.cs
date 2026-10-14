using GCStock.Application.Abstractions;
using GCStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Database;

/// <summary>
/// Popula o banco com os mesmos donos/empresas de demonstração que existiam no MockDataService do front-end,
/// para que o login continue funcionando com as mesmas credenciais durante a migração do mock para a API real.
/// Roda apenas uma vez (idempotente: não faz nada se já existir ao menos um Administrator).
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(GCStockDbContext db, IPasswordHasher passwordHasher, CancellationToken ct = default)
    {
        if (await db.Administrators.AnyAsync(ct)) return;

        var italo = new Administrator
        {
            Id = Guid.Parse("00000000-0000-0000-0000-00000000a001"),
            Name = "Ítalo Silva",
            Email = "italodasilvaa@gmail.com",
            PasswordHash = passwordHasher.Hash("123"),
            CreatedAt = new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc),
        };
        var roberto = new Administrator
        {
            Id = Guid.Parse("00000000-0000-0000-0000-00000000a002"),
            Name = "Roberto Mendonça",
            Email = "contato@angusprime.com.br",
            PasswordHash = passwordHasher.Hash("123"),
            CreatedAt = new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc),
        };
        var mariana = new Administrator
        {
            Id = Guid.Parse("00000000-0000-0000-0000-00000000a003"),
            Name = "Mariana Duarte",
            Email = "diretoria@braseirodosul.com.br",
            PasswordHash = passwordHasher.Hash("123"),
            CreatedAt = new DateTime(2025, 5, 20, 0, 0, 0, DateTimeKind.Utc),
        };
        db.Administrators.AddRange(italo, roberto, mariana);

        var mcFrig = new Company
        {
            Id = Guid.Parse("00000000-0000-0000-0000-00000000c001"),
            CorporateName = "M&C Frig Distribuição de Carnes Ltda",
            TradeName = "M&C Frig Carnes Nobres",
            Document = "12.345.678/0001-90",
            OwnerId = italo.Id,
            Phone = "(11) 98765-4321",
            Segment = "Açougue Boutique & Kits de Churrasco",
            City = "São Paulo",
            State = "SP",
            CreatedAt = new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            Active = true,
            Plan = "enterprise",
        };
        var angusPrime = new Company
        {
            Id = Guid.Parse("00000000-0000-0000-0000-00000000c002"),
            CorporateName = "Angus Prime Cortes Especiais EIRELI",
            TradeName = "Angus Prime Steakhouse & Boutique",
            Document = "28.910.441/0001-30",
            OwnerId = roberto.Id,
            Phone = "(19) 99122-3344",
            Segment = "Casa de Carnes & Cortes Nobres",
            City = "Campinas",
            State = "SP",
            CreatedAt = new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            Active = true,
            Plan = "pro",
        };
        var braseiroDoSul = new Company
        {
            Id = Guid.Parse("00000000-0000-0000-0000-00000000c003"),
            CorporateName = "Frigorífico e Abatedouro Braseiro do Sul S/A",
            TradeName = "Frigorífico Braseiro do Sul",
            Document = "45.890.112/0001-88",
            OwnerId = mariana.Id,
            Phone = "(16) 98111-2233",
            Segment = "Frigorífico & Distribuidora B2B",
            City = "Ribeirão Preto",
            State = "SP",
            CreatedAt = new DateTime(2025, 5, 20, 0, 0, 0, DateTimeKind.Utc),
            Active = true,
            Plan = "enterprise",
        };
        var mcFrigLitoral = new Company
        {
            Id = Guid.Parse("00000000-0000-0000-0000-00000000c004"),
            CorporateName = "M&C Frig Litoral Comércio de Carnes Ltda",
            TradeName = "M&C Frig Litoral",
            Document = "12.345.678/0004-15",
            OwnerId = italo.Id,
            Phone = "(13) 98877-6655",
            Segment = "Açougue Boutique & Kits de Churrasco",
            City = "Santos",
            State = "SP",
            CreatedAt = new DateTime(2025, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            Active = true,
            Plan = "pro",
        };
        var companies = new[] { mcFrig, angusPrime, braseiroDoSul, mcFrigLitoral };
        db.Companies.AddRange(companies);

        foreach (var company in companies)
        {
            db.Branches.Add(new Branch
            {
                CompanyId = company.Id,
                Name = company.TradeName,
                Location = $"{company.City} / {company.State}",
                Cnpj = company.Document,
                OperatorsCount = 3,
                Active = true,
                Type = "matriz",
            });
            db.AlertSettings.Add(new AlertSetting
            {
                CompanyId = company.Id,
                Recipients = ["carlos.gerencia@mcfrig.com.br", "+55 (11) 98844-2190 (Comprador Frigorífico)"],
            });
        }

        // Dados de exemplo apenas na empresa principal, para o dashboard não abrir vazio.
        SeedDemoDataFor(db, mcFrig.Id);

        await db.SaveChangesAsync(ct);
    }

    private static void SeedDemoDataFor(GCStockDbContext db, Guid companyId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        db.Products.AddRange(
            new Product
            {
                CompanyId = companyId,
                Sku = "CAR-101",
                Code = "#CAR-101",
                Ean = "7898912100458",
                Name = "Picanha Angus Premium",
                Category = "Carnes Bovinas",
                Unit = "kg",
                Stock = 42,
                MinStock = 15,
                CostPrice = 39.9m,
                SellingPrice = 79.9m,
                IsPerishable = true,
                AlertDays = 3,
                StorageTemp = "-2°C a 2°C",
                NearestExpiry = today.AddDays(12),
                ExpiryDaysRemaining = 12,
                ExpiryStatus = "normal",
                LotNumber = "LT-501",
                Supplier = "Minerva Foods S.A.",
                ImageUrl = string.Empty,
            },
            new Product
            {
                CompanyId = companyId,
                Sku = "CAR-102",
                Code = "#CAR-102",
                Ean = "7898912100465",
                Name = "Costela Bovina Janela",
                Category = "Carnes Bovinas",
                Unit = "kg",
                Stock = 8,
                MinStock = 15,
                CostPrice = 18.5m,
                SellingPrice = 34.9m,
                IsPerishable = true,
                AlertDays = 3,
                StorageTemp = "-2°C a 2°C",
                NearestExpiry = today.AddDays(2),
                ExpiryDaysRemaining = 2,
                ExpiryStatus = "critico",
                LotNumber = "LT-502",
                Supplier = "Minerva Foods S.A.",
                ImageUrl = string.Empty,
            });

        db.Clients.Add(new Client
        {
            CompanyId = companyId,
            Name = "Restaurante Braseiro Ltda",
            Document = "12.345.678/0001-00",
            Phone = "(11) 91234-5678",
            Email = "compras@restaurantebraseiro.com.br",
            Type = "pj",
            FidelityPoints = 120,
            CreditLimit = 5000,
            TotalSpent = 12500,
            FavoriteCuts = "Picanha, Costela",
        });

        db.PaymentFeeSettings.AddRange(
            new PaymentFeeSetting
            {
                CompanyId = companyId,
                Method = "Débito",
                Subtitle = "Cartão de débito à vista",
                Channel = "TEF Balcão",
                FeePercent = 1.39m,
                SettlementTerm = "D+1",
                Active = true,
                Icon = "credit_card",
                SortOrder = 0,
            },
            new PaymentFeeSetting
            {
                CompanyId = companyId,
                Method = "Crédito à Vista",
                Subtitle = "Cartão de crédito à vista",
                Channel = "TEF Balcão",
                FeePercent = 2.99m,
                SettlementTerm = "D+30",
                Active = true,
                Icon = "credit_card",
                SortOrder = 1,
            },
            new PaymentFeeSetting
            {
                CompanyId = companyId,
                Method = "Pix",
                Subtitle = "Transferência instantânea",
                Channel = "QR Code Balcão",
                FeePercent = 0,
                SettlementTerm = "D+0",
                Active = true,
                Icon = "pix",
                SortOrder = 2,
            });
    }
}
