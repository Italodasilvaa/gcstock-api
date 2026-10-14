using GCStock.Application.Audit;
using GCStock.Application.Abstractions;
using GCStock.Application.Catalog;
using GCStock.Domain.Entities;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

public class ProductService(GCStockDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit, ICategoryService categories) : IProductService
{
    public async Task<List<ProductDto>> GetAllAsync(CancellationToken ct)
    {
        return await db.Products
            .Where(p => p.CompanyId == currentUser.CompanyId)
            .Select(p => ToDto(p))
            .ToListAsync(ct);
    }

    public async Task<ProductDto> SaveAsync(SaveProductRequest request, CancellationToken ct)
    {
        if (request.Id.HasValue)
        {
            var existing = await db.Products.FirstOrDefaultAsync(p => p.Id == request.Id && p.CompanyId == currentUser.CompanyId, ct);
            if (existing is not null)
            {
                if (request.Sku is not null) existing.Sku = request.Sku;
                if (request.Name is not null) existing.Name = request.Name;
                if (request.Category is not null) existing.Category = request.Category;
                if (request.Unit is not null) existing.Unit = request.Unit;
                if (request.Stock is not null) existing.Stock = request.Stock.Value;
                if (request.MinStock is not null) existing.MinStock = request.MinStock.Value;
                if (request.CostPrice is not null) existing.CostPrice = request.CostPrice.Value;
                if (request.SellingPrice is not null) existing.SellingPrice = request.SellingPrice.Value;
                if (request.IsPerishable is not null) existing.IsPerishable = request.IsPerishable.Value;
                if (request.AlertDays is not null) existing.AlertDays = request.AlertDays.Value;
                if (request.StorageTemp is not null) existing.StorageTemp = request.StorageTemp;
                if (request.NearestExpiry is not null) existing.NearestExpiry = request.NearestExpiry.Value;
                if (request.ExpiryDaysRemaining is not null) existing.ExpiryDaysRemaining = request.ExpiryDaysRemaining.Value;
                if (request.ExpiryStatus is not null) existing.ExpiryStatus = request.ExpiryStatus;
                if (request.LotNumber is not null) existing.LotNumber = request.LotNumber;
                if (request.Supplier is not null) existing.Supplier = request.Supplier;
                if (request.ImageUrl is not null) existing.ImageUrl = request.ImageUrl;
                if (request.SubLocation is not null) existing.SubLocation = request.SubLocation;
                if (request.IsPromo is not null) existing.IsPromo = request.IsPromo.Value;

                await db.SaveChangesAsync(ct);
                await categories.EnsureExistsAsync(existing.Category, ct);
                await audit.RecordAsync("editou", "Produto", existing.Name, null, ct);
                return ToDto(existing);
            }
        }

        var rnd = Random.Shared;
        var skuSeed = "CAR-" + rnd.Next(100, 1000);
        var saved = new Product
        {
            CompanyId = currentUser.CompanyId,
            Sku = request.Sku ?? skuSeed,
            Code = "#" + (request.Sku ?? skuSeed),
            Ean = request.Ean ?? "7898912" + rnd.Next(100000, 999999),
            Name = request.Name ?? "Novo Produto Frigorífico",
            Category = request.Category ?? "Carnes Bovinas",
            Unit = request.Unit ?? "kg",
            Stock = request.Stock ?? 0,
            MinStock = request.MinStock ?? 15,
            CostPrice = request.CostPrice ?? 0,
            SellingPrice = request.SellingPrice ?? 0,
            IsPerishable = request.IsPerishable ?? true,
            AlertDays = request.AlertDays ?? 3,
            StorageTemp = request.StorageTemp ?? "-2°C a 2°C",
            // Sem validade informada: mesmo estado de "produto sem lote" usado pelo PurchaseService.
            // A validade real passa a vir dos lotes lançados em Compras.
            NearestExpiry = request.NearestExpiry ?? DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1),
            ExpiryDaysRemaining = request.ExpiryDaysRemaining ?? 365,
            ExpiryStatus = request.ExpiryStatus ?? "longo",
            LotNumber = request.LotNumber ?? string.Empty,
            Supplier = request.Supplier ?? "Minerva Foods S.A.",
            ImageUrl = request.ImageUrl ?? string.Empty,
            SubLocation = request.SubLocation,
            IsPromo = request.IsPromo ?? false,
        };
        db.Products.Add(saved);
        await db.SaveChangesAsync(ct);
        await categories.EnsureExistsAsync(saved.Category, ct);
        await audit.RecordAsync("criou", "Produto", saved.Name, null, ct);
        return ToDto(saved);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var target = await db.Products.FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == currentUser.CompanyId, ct);
        if (target is null) return;

        db.Products.Remove(target);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("excluiu", "Produto", target.Name, null, ct);
    }

    private static ProductDto ToDto(Product p) => new(
        p.Id, p.Sku, p.Code, p.Ean, p.Name, p.Category, p.Unit, p.Stock, p.MinStock, p.CostPrice, p.SellingPrice,
        p.IsPerishable, p.AlertDays, p.StorageTemp, p.NearestExpiry, p.ExpiryDaysRemaining, p.ExpiryStatus,
        p.LotNumber, p.Supplier, p.ImageUrl, p.SubLocation, p.IsPromo);
}
