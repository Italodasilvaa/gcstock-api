using GCStock.Application.Abstractions;
using GCStock.Application.Inventory;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

public class InventoryService(GCStockDbContext db, ICurrentUserContext currentUser) : IInventoryService
{
    /// <summary>
    /// Estoque por lote. Todo produto com estoque aparece: a parte que veio de compras é listada por lote e o que está
    /// no cadastro do produto sem lote (estoque digitado manualmente, por exemplo) aparece como uma linha "Sem lote",
    /// para que a soma da tela de Estoque sempre bata com o estoque da tela de Produtos.
    /// </summary>
    public async Task<List<StockBatchDto>> GetBatchesAsync(CancellationToken ct)
    {
        // Lotes totalmente consumidos pelas vendas não aparecem no estoque.
        var batches = await db.StockBatches
            .Where(b => b.CompanyId == currentUser.CompanyId && b.AvailableKg > 0)
            .OrderBy(b => b.ExpiryDate)
            .Select(b => new StockBatchDto(
                b.Id, b.ProductId, b.ProductName, b.Sku, b.LotNumber, b.AvailableKg,
                b.EntryDate, b.ExpiryDate, b.DaysRemaining, b.FiscalDoc, b.Supplier, b.Status, b.Unit, b.UnitCost,
                db.PurchaseEntries.Where(p => p.Id == b.PurchaseEntryId).Select(p => p.OrderCode).FirstOrDefault() ?? ""))
            .ToListAsync(ct);

        var batchedByProduct = batches
            .GroupBy(b => b.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(b => b.AvailableKg));

        var products = await db.Products
            .Where(p => p.CompanyId == currentUser.CompanyId && p.Stock > 0)
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var product in products)
        {
            var withoutLot = product.Stock - batchedByProduct.GetValueOrDefault(product.Id);
            if (withoutLot <= 0) continue;

            var daysRemaining = product.NearestExpiry.DayNumber - today.DayNumber;
            batches.Add(new StockBatchDto(
                product.Id, product.Id, product.Name, product.Sku, "Sem lote", withoutLot,
                today, product.NearestExpiry, daysRemaining, "—",
                string.IsNullOrWhiteSpace(product.Supplier) ? "—" : product.Supplier,
                daysRemaining < 0 ? "vencido"
                    : product.ExpiryStatus == "critico" ? "urgente"
                    : product.ExpiryStatus == "normal" ? "regular"
                    : "novo",
                product.Unit, product.CostPrice));
        }

        return batches.OrderBy(b => b.ExpiryDate).ToList();
    }

    public async Task<List<StockMovementDto>> GetMovementsAsync(CancellationToken ct)
    {
        return await db.StockMovements
            .Where(m => m.CompanyId == currentUser.CompanyId)
            .OrderByDescending(m => m.Timestamp)
            .Select(m => new StockMovementDto(
                m.Id, m.Timestamp, m.Type, m.Title, m.DocInfo, m.AmountKg, m.Description, m.BadgeLabel, m.AuditLink, m.Unit))
            .ToListAsync(ct);
    }
}
