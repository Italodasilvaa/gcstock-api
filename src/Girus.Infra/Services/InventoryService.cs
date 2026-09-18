using Girus.Application.Abstractions;
using Girus.Application.Inventory;
using Girus.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace Girus.Infra.Services;

public class InventoryService(GirusDbContext db, ICurrentUserContext currentUser) : IInventoryService
{
    public async Task<List<StockBatchDto>> GetBatchesAsync(CancellationToken ct)
    {
        return await db.StockBatches
            .Where(b => b.CompanyId == currentUser.CompanyId)
            .OrderBy(b => b.ExpiryDate)
            .Select(b => new StockBatchDto(
                b.Id, b.ProductId, b.ProductName, b.Sku, b.LotNumber, b.AvailableKg,
                b.EntryDate, b.ExpiryDate, b.DaysRemaining, b.FiscalDoc, b.Supplier, b.Status))
            .ToListAsync(ct);
    }

    public async Task<List<StockMovementDto>> GetMovementsAsync(CancellationToken ct)
    {
        return await db.StockMovements
            .Where(m => m.CompanyId == currentUser.CompanyId)
            .OrderByDescending(m => m.Timestamp)
            .Select(m => new StockMovementDto(
                m.Id, m.Timestamp, m.Type, m.Title, m.DocInfo, m.AmountKg, m.Description, m.BadgeLabel, m.AuditLink))
            .ToListAsync(ct);
    }
}
