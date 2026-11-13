using GCStock.Domain.Entities;
using GCStock.Domain.Services;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

/// <summary>
/// Quando o custo médio de produtos muda (compra, edição/cancelamento de compra, ajuste manual), recalcula os kits que
/// usam esses produtos: custo de cada componente, custo total, margem e (se todos em kg) peso. O preço de venda não é
/// alterado e as vendas já feitas guardam o próprio custo (SaleItem), então não mudam. Roda na mesma transação e no mesmo
/// SaveChanges da mudança de custo, usando os produtos já rastreados (custo novo, ainda não gravado).
/// </summary>
internal static class KitCostRecalculator
{
    public static async Task RecalculateForProductsAsync(
        GCStockDbContext db, Guid companyId, string createdBy, IReadOnlyCollection<Product> changedProducts, CancellationToken ct)
    {
        if (changedProducts.Count == 0) return;

        var changedIds = changedProducts.Select(p => p.Id).ToList();
        var kits = await db.Kits
            .Include(k => k.Components)
            .Where(k => k.CompanyId == companyId && k.Components.Any(c => changedIds.Contains(c.ProductId)))
            .ToListAsync(ct);
        if (kits.Count == 0) return;

        // Custos atuais: os produtos que acabaram de mudar (em memória) + os demais componentes, lidos do banco.
        var products = changedProducts.ToDictionary(p => p.Id);
        var otherIds = kits.SelectMany(k => k.Components.Select(c => c.ProductId)).Distinct().Where(id => !products.ContainsKey(id)).ToList();
        if (otherIds.Count > 0)
        {
            foreach (var product in await db.Products.Where(p => p.CompanyId == companyId && otherIds.Contains(p.Id)).ToListAsync(ct))
            {
                products[product.Id] = product;
            }
        }

        foreach (var kit in kits)
        {
            var previousCost = kit.CostPrice;
            var previousMargin = kit.MarginPercent;
            if (!KitPricing.Recalculate(kit, products)) continue;

            db.KitCostHistories.Add(new KitCostHistory
            {
                CompanyId = companyId,
                KitId = kit.Id,
                KitName = kit.Name,
                PreviousCost = previousCost,
                NewCost = kit.CostPrice,
                PreviousMarginPercent = previousMargin,
                NewMarginPercent = kit.MarginPercent,
                SuggestedPrice = kit.SuggestedPrice,
                Source = "recalculo_automatico",
                TriggerProduct = string.Join(", ", kit.Components.Where(c => changedIds.Contains(c.ProductId)).Select(c => c.Name).Distinct()),
                CreatedBy = createdBy,
            });
        }
    }
}
