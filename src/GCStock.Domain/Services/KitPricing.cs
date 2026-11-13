using GCStock.Domain.Entities;

namespace GCStock.Domain.Services;

/// <summary>
/// Regra única de custo e margem de kit. O custo do kit é sempre a soma de (quantidade x custo médio ATUAL do produto)
/// de cada componente; o preço de venda nunca é tocado. Quem grava o kit (cadastro) e quem reage a uma mudança de
/// custo (compra, ajuste) usam esta mesma conta, para não haver duas fontes de verdade.
/// </summary>
public static class KitPricing
{
    /// <summary>Margem em % sobre o preço de venda, com 1 casa (convenção já usada nos kits). Sem preço, margem 0.</summary>
    public static decimal Margin(decimal price, decimal cost) =>
        price > 0 ? Math.Round((price - cost) / price * 100m, 1, MidpointRounding.AwayFromZero) : 0m;

    /// <summary>Menor preço de venda (arredondado para cima, ao centavo) que entrega a margem desejada sobre o custo.</summary>
    public static decimal PriceForMargin(decimal cost, decimal marginPercent) =>
        marginPercent >= 100m ? 0m : Math.Ceiling(cost / (1m - marginPercent / 100m) * 100m) / 100m;

    /// <summary>
    /// Quantos kits dá para montar agora: o menor (estoque ÷ quantidade) entre os componentes, arredondado para baixo.
    /// É o que limita a venda de verdade; o número guardado no kit não entra na conta. Produto que não existe mais
    /// (ou sem estoque) zera a disponibilidade. Kit sem componentes não tem como ser montado: 0.
    /// </summary>
    public static decimal MaxKits(Kit kit, IReadOnlyDictionary<Guid, Product> products)
    {
        if (kit.Components.Count == 0) return 0m;

        var max = decimal.MaxValue;
        foreach (var component in kit.Components)
        {
            if (component.Quantity <= 0) continue;
            var stock = products.TryGetValue(component.ProductId, out var product) ? Math.Max(0m, product.Stock) : 0m;
            max = Math.Min(max, Math.Floor(stock / component.Quantity));
        }

        return max == decimal.MaxValue ? 0m : max;
    }

    /// <summary>Faixa de disponibilidade exibida: "alerta" (0 a 2 kits) | "disponivel" (3 a 9) | "amplo" (10 ou mais).</summary>
    public static string StockStatus(decimal kitsAvailable) =>
        kitsAvailable >= 10m ? "amplo" : kitsAvailable >= 3m ? "disponivel" : "alerta";

    /// <summary>
    /// Atualiza os componentes (custo e subtotal), o custo total, a margem e — quando todos os componentes são medidos em kg —
    /// o peso total. Componente cujo produto não está em <paramref name="products"/> mantém o custo gravado.
    /// Devolve true se custo ou margem do kit mudaram.
    /// </summary>
    public static bool Recalculate(Kit kit, IReadOnlyDictionary<Guid, Product> products)
    {
        var previousCost = kit.CostPrice;
        var previousMargin = kit.MarginPercent;

        if (kit.Components.Count > 0)
        {
            foreach (var component in kit.Components)
            {
                if (products.TryGetValue(component.ProductId, out var product))
                {
                    component.UnitCost = product.CostPrice;
                }
                component.Subtotal = Math.Round(component.Quantity * component.UnitCost, 2, MidpointRounding.AwayFromZero);
            }

            kit.CostPrice = kit.Components.Sum(c => c.Subtotal);

            // Peso só é recalculado quando dá para somar (todos em kg); com unidades/pacotes misturados, vale o informado.
            if (kit.Components.All(c => string.Equals(c.Unit, "kg", StringComparison.OrdinalIgnoreCase)))
            {
                kit.TotalWeightKg = Math.Round(kit.Components.Sum(c => c.Quantity), 3, MidpointRounding.AwayFromZero);
            }
        }

        kit.MarginPercent = Margin(kit.SuggestedPrice, kit.CostPrice);
        return kit.CostPrice != previousCost || kit.MarginPercent != previousMargin;
    }
}
