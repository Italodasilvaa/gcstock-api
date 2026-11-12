using GCStock.Domain.Entities;
using GCStock.Infra.Database;

namespace GCStock.Infra.Services;

/// <summary>
/// Histórico de custo médio dos produtos. Tire um Snapshot antes de mexer no custo e chame RecordChanges depois:
/// só os produtos cujo custo realmente mudou ganham uma linha (de quanto, para quanto, por quê e quem fez).
/// </summary>
internal static class CostHistory
{
    /// <summary>Casas do custo unitário (a coluna é decimal(18,4)); arredondar só aqui evita erro acumulado entre compras.</summary>
    public const int Decimals = 4;

    public static decimal Round(decimal cost) => Math.Round(cost, Decimals, MidpointRounding.AwayFromZero);

    public static Dictionary<Guid, decimal> Snapshot(IEnumerable<Product> products) =>
        products.ToDictionary(p => p.Id, p => p.CostPrice);

    public static void RecordChanges(
        GCStockDbContext db, Guid companyId, string createdBy, Dictionary<Guid, decimal> before,
        IEnumerable<Product> products, string source, PurchaseEntry? purchase)
    {
        foreach (var product in products)
        {
            if (!before.TryGetValue(product.Id, out var previous) || previous == product.CostPrice) continue;

            db.ProductCostHistories.Add(new ProductCostHistory
            {
                CompanyId = companyId,
                ProductId = product.Id,
                ProductName = product.Name,
                PreviousCost = previous,
                NewCost = product.CostPrice,
                Source = source,
                PurchaseEntryId = purchase?.Id,
                PurchaseCode = purchase?.OrderCode,
                CreatedBy = createdBy,
            });
        }
    }
}
