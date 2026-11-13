using GCStock.Application.Abstractions;
using GCStock.Application.Inventory;
using GCStock.Domain.Services;
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
        var lots = await db.StockBatches
            .Where(b => b.CompanyId == currentUser.CompanyId && b.AvailableKg > 0)
            .OrderBy(b => b.ExpiryDate)
            .ToListAsync(ct);
        var purchaseIds = lots.Where(b => b.PurchaseEntryId.HasValue).Select(b => b.PurchaseEntryId!.Value).Distinct().ToList();
        var orderCodes = purchaseIds.Count > 0
            ? await db.PurchaseEntries
                .Where(p => p.CompanyId == currentUser.CompanyId && purchaseIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.OrderCode, ct)
            : [];

        var products = await db.Products
            .Where(p => p.CompanyId == currentUser.CompanyId)
            .ToDictionaryAsync(p => p.Id, ct);

        // Dias restantes e status são calculados de hoje, não lidos do que foi gravado na entrada (que envelhece).
        var today = ExpiryRules.Today(DateTime.UtcNow);
        var batches = lots.Select(b =>
        {
            var days = ExpiryRules.DaysRemaining(b.ExpiryDate, today);
            var alertDays = products.TryGetValue(b.ProductId, out var owner) ? owner.AlertDays : 3;
            return new StockBatchDto(
                b.Id, b.ProductId, b.ProductName, b.Sku, b.LotNumber, b.AvailableKg,
                b.EntryDate, b.ExpiryDate, days, b.FiscalDoc, b.Supplier, ExpiryRules.LotStatus(days, alertDays), b.Unit, b.UnitCost,
                b.PurchaseEntryId.HasValue && orderCodes.TryGetValue(b.PurchaseEntryId.Value, out var code) ? code : "");
        }).ToList();

        var batchedByProduct = batches
            .GroupBy(b => b.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(b => b.AvailableKg));

        foreach (var product in products.Values.Where(p => p.Stock > 0))
        {
            var withoutLot = product.Stock - batchedByProduct.GetValueOrDefault(product.Id);
            if (withoutLot <= 0) continue;

            var daysRemaining = ExpiryRules.DaysRemaining(product.NearestExpiry, today);
            batches.Add(new StockBatchDto(
                product.Id, product.Id, product.Name, product.Sku, "Sem lote", withoutLot,
                today, product.NearestExpiry, daysRemaining, "—",
                string.IsNullOrWhiteSpace(product.Supplier) ? "—" : product.Supplier,
                ExpiryRules.LotStatus(daysRemaining, product.AlertDays),
                product.Unit, product.CostPrice));
        }

        return batches.OrderBy(b => b.ExpiryDate).ToList();
    }

    public async Task<List<StockMovementDto>> GetMovementsAsync(CancellationToken ct)
    {
        return await db.StockMovements
            .Where(m => m.CompanyId == currentUser.CompanyId)
            .OrderByDescending(m => m.Timestamp)
            // Histórico cresce a cada venda e compra: devolve as mais recentes (as telas só exibem o recente).
            .Take(2000)
            .Select(m => new StockMovementDto(
                m.Id, m.Timestamp, m.Type, m.Title, m.DocInfo, m.AmountKg, m.Description, m.BadgeLabel, m.AuditLink, m.Unit))
            .ToListAsync(ct);
    }
}
