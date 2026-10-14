using GCStock.Application.Audit;
using GCStock.Application.Abstractions;
using GCStock.Application.Catalog;
using GCStock.Domain.Entities;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

public class KitService(GCStockDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit) : IKitService
{
    public async Task<List<KitDto>> GetAllAsync(CancellationToken ct)
    {
        var kits = await db.Kits
            .Include(k => k.Components)
            .Where(k => k.CompanyId == currentUser.CompanyId)
            .ToListAsync(ct);
        return kits.Select(ToDto).ToList();
    }

    public async Task<KitDto> SaveAsync(SaveKitRequest request, CancellationToken ct)
    {
        if (request.Id.HasValue)
        {
            var existing = await db.Kits
                .Include(k => k.Components)
                .FirstOrDefaultAsync(k => k.Id == request.Id && k.CompanyId == currentUser.CompanyId, ct);
            if (existing is not null)
            {
                if (request.Name is not null) existing.Name = request.Name;
                if (request.Tag is not null) existing.Tag = request.Tag;
                if (request.ServeLabel is not null) existing.ServeLabel = request.ServeLabel;
                if (request.Description is not null) existing.Description = request.Description;
                if (request.SuggestedPrice is not null) existing.SuggestedPrice = request.SuggestedPrice.Value;
                if (request.CostPrice is not null) existing.CostPrice = request.CostPrice.Value;
                if (request.MarginPercent is not null) existing.MarginPercent = request.MarginPercent.Value;
                if (request.TotalWeightKg is not null) existing.TotalWeightKg = request.TotalWeightKg.Value;
                if (request.ImageUrl is not null) existing.ImageUrl = request.ImageUrl;
                if (request.ComponentsSummary is not null) existing.ComponentsSummary = request.ComponentsSummary;
                if (request.StockAvailable is not null) existing.StockAvailable = request.StockAvailable.Value;
                if (request.Components is not null)
                {
                    db.KitComponents.RemoveRange(existing.Components);
                    existing.Components = request.Components.Select(ToEntity).ToList();
                }

                await db.SaveChangesAsync(ct);
                await audit.RecordAsync("editou", "Kit de Churrasco", existing.Name, null, ct);
                return ToDto(existing);
            }
        }

        var count = await db.Kits.CountAsync(k => k.CompanyId == currentUser.CompanyId, ct);
        var saved = new Kit
        {
            CompanyId = currentUser.CompanyId,
            Code = "KIT-00" + (count + 1),
            Name = request.Name ?? "Novo Kit de Churrasco",
            Tag = request.Tag ?? "Combo Especial",
            ServeLabel = request.ServeLabel ?? "Serve 6 a 8 pessoas",
            Description = request.Description ?? "Cortes selecionados para churrasco.",
            SuggestedPrice = request.SuggestedPrice ?? 299.0m,
            CostPrice = request.CostPrice ?? 170.0m,
            MarginPercent = request.MarginPercent ?? 43.1m,
            TotalWeightKg = request.TotalWeightKg ?? 3.5m,
            ImageUrl = request.ImageUrl ?? string.Empty,
            ComponentsSummary = request.ComponentsSummary ?? "Picanha Angus, Ancho, Pão de Alho.",
            StockAvailable = request.StockAvailable ?? 10,
            StockStatus = "disponivel",
            BarcodeEan = "78994582000" + Random.Shared.Next(10, 100),
            Components = request.Components?.Select(ToEntity).ToList() ?? [],
        };
        db.Kits.Add(saved);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("criou", "Kit de Churrasco", saved.Name, null, ct);
        return ToDto(saved);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var target = await db.Kits.FirstOrDefaultAsync(k => k.Id == id && k.CompanyId == currentUser.CompanyId, ct);
        if (target is null) return;

        db.Kits.Remove(target);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("excluiu", "Kit de Churrasco", target.Name, null, ct);
    }

    private static KitComponent ToEntity(KitComponentDto c) => new()
    {
        ProductId = c.ProductId,
        Name = c.Name,
        Code = c.Code,
        LotNumber = c.LotNumber,
        Quantity = c.Quantity,
        Unit = c.Unit,
        UnitCost = c.UnitCost,
        Subtotal = c.Subtotal,
        AvailableStock = c.AvailableStock,
    };

    private static KitDto ToDto(Kit k) => new(
        k.Id, k.Code, k.Name, k.Tag, k.ServeLabel, k.Description, k.SuggestedPrice, k.CostPrice, k.MarginPercent,
        k.TotalWeightKg, k.ImageUrl, k.ComponentsSummary,
        k.Components.Select(c => new KitComponentDto(
            c.ProductId, c.Name, c.Code, c.LotNumber, c.Quantity, c.Unit, c.UnitCost, c.Subtotal, c.AvailableStock)).ToList(),
        k.StockAvailable, k.StockStatus, k.BarcodeEan);
}
