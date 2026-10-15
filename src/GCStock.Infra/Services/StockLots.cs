using GCStock.Domain.Entities;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

/// <summary>
/// Mantém os lotes coerentes com o estoque do produto. O estoque do produto (Product.Stock) é a fonte da verdade:
/// vendas, edição de compras e ajustes manuais mexem nele, e os lotes (StockBatch) nunca podem somar MAIS que ele.
/// O excesso é consumido primeiro dos lotes que vencem antes (FEFO). Lotes nunca são aumentados aqui: estoque sem
/// lote (cadastro manual, por exemplo) simplesmente não tem lote.
/// </summary>
internal static class StockLots
{
    /// <summary>
    /// Tira `quantity` do produto consumindo primeiro os lotes que vencem antes (FEFO) e devolve de quais lotes saiu,
    /// para o cancelamento da venda poder devolver ao mesmo lote. O que não estiver coberto por lotes não é registrado.
    /// </summary>
    public static async Task<List<(Guid LotId, decimal Qty)>> ConsumeAsync(
        GCStockDbContext db, Guid companyId, Guid productId, decimal quantity, CancellationToken ct)
    {
        var taken = new List<(Guid LotId, decimal Qty)>();
        if (quantity <= 0) return taken;

        var fromDb = await db.StockBatches
            .Where(b => b.CompanyId == companyId && b.ProductId == productId)
            .ToListAsync(ct);
        var lots = db.StockBatches.Local
            .Where(b => b.CompanyId == companyId && b.ProductId == productId)
            .Union(fromDb)
            .Where(b => b.AvailableKg > 0)
            .OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.EntryDate)
            .ToList();

        var remaining = quantity;
        foreach (var lot in lots)
        {
            if (remaining <= 0) break;
            var take = Math.Min(lot.AvailableKg, remaining);
            lot.AvailableKg -= take;
            remaining -= take;
            taken.Add((lot.Id, take));
        }

        return taken;
    }

    /// <summary>Reduz os lotes dos produtos informados até que a soma deles não passe do estoque do produto.</summary>
    public static async Task ReconcileAsync(
        GCStockDbContext db, Guid companyId, IReadOnlyCollection<Product> products, CancellationToken ct)
    {
        if (products.Count == 0) return;

        var ids = products.Select(p => p.Id).ToList();

        // Lotes já rastreados (inclusive os recém-criados e ainda não salvos) + os do banco, sem repetir instâncias.
        var fromDb = await db.StockBatches
            .Where(b => b.CompanyId == companyId && ids.Contains(b.ProductId))
            .ToListAsync(ct);
        var lots = db.StockBatches.Local
            .Where(b => b.CompanyId == companyId && ids.Contains(b.ProductId))
            .Union(fromDb)
            .Where(b => b.AvailableKg > 0)
            .ToList();

        foreach (var product in products)
        {
            var productLots = lots
                .Where(b => b.ProductId == product.Id)
                .OrderBy(b => b.ExpiryDate)
                .ThenBy(b => b.EntryDate)
                .ToList();

            var excess = productLots.Sum(b => b.AvailableKg) - Math.Max(0, product.Stock);
            foreach (var lot in productLots)
            {
                if (excess <= 0) break;
                var take = Math.Min(lot.AvailableKg, excess);
                lot.AvailableKg -= take;
                excess -= take;
            }
        }
    }
}
