using GCStock.Domain.Entities;
using GCStock.Domain.Services;

namespace GCStock.UnitTests.Domain;

public class KitPricingTests
{
    private static Product Product(decimal cost) => new() { Name = "Produto", CostPrice = cost, Stock = 10 };

    private static KitComponent Component(Product product, decimal quantity, string unit = "kg") => new()
    {
        ProductId = product.Id, Name = product.Name, Quantity = quantity, Unit = unit, UnitCost = 0,
    };

    private static Dictionary<Guid, Product> Catalog(params Product[] products) => products.ToDictionary(p => p.Id);

    [Fact]
    public void Custo_do_kit_e_a_soma_de_quantidade_vezes_custo_atual_de_cada_componente()
    {
        var picanha = Product(60m);
        var linguica = Product(21m);
        var kit = new Kit { SuggestedPrice = 200m, Components = [Component(picanha, 1m), Component(linguica, 0.8m)] };

        KitPricing.Recalculate(kit, Catalog(picanha, linguica));

        Assert.Equal(60m, kit.Components[0].Subtotal);
        Assert.Equal(16.8m, kit.Components[1].Subtotal);
        Assert.Equal(76.8m, kit.CostPrice);
        Assert.Equal(61.6m, kit.MarginPercent);
    }

    [Fact]
    public void Nova_compra_da_linguica_recalcula_o_custo_e_a_margem_sem_mexer_no_preco_de_venda()
    {
        var picanha = Product(60m);
        var linguica = Product(21m);
        var kit = new Kit { SuggestedPrice = 100m, Components = [Component(picanha, 1m), Component(linguica, 1m)] };
        KitPricing.Recalculate(kit, Catalog(picanha, linguica));
        Assert.Equal(81m, kit.CostPrice);
        Assert.Equal(19m, kit.MarginPercent);

        // Custo médio da linguiça sobe de 21 para 25.
        linguica.CostPrice = 25m;
        var changed = KitPricing.Recalculate(kit, Catalog(picanha, linguica));

        Assert.True(changed);
        Assert.Equal(85m, kit.CostPrice);
        Assert.Equal(15m, kit.MarginPercent);
        Assert.Equal(100m, kit.SuggestedPrice);
    }

    [Fact]
    public void Sem_mudanca_de_custo_nao_ha_alteracao()
    {
        var picanha = Product(60m);
        var kit = new Kit { SuggestedPrice = 100m, Components = [Component(picanha, 1m)] };
        KitPricing.Recalculate(kit, Catalog(picanha));

        Assert.False(KitPricing.Recalculate(kit, Catalog(picanha)));
    }

    [Fact]
    public void Peso_so_e_recalculado_quando_todos_os_componentes_sao_kg()
    {
        var a = Product(10m);
        var b = Product(10m);
        var allKg = new Kit { SuggestedPrice = 100m, TotalWeightKg = 99m, Components = [Component(a, 1m), Component(b, 0.5m)] };
        KitPricing.Recalculate(allKg, Catalog(a, b));
        Assert.Equal(1.5m, allKg.TotalWeightKg);

        var mixed = new Kit { SuggestedPrice = 100m, TotalWeightKg = 3.5m, Components = [Component(a, 1m), Component(b, 2m, "un")] };
        KitPricing.Recalculate(mixed, Catalog(a, b));
        Assert.Equal(3.5m, mixed.TotalWeightKg);
    }

    [Fact]
    public void Componente_cujo_produto_nao_existe_mais_mantem_o_custo_gravado()
    {
        var gone = Product(10m);
        var component = Component(gone, 2m);
        component.UnitCost = 7m;
        var kit = new Kit { SuggestedPrice = 100m, Components = [component] };

        KitPricing.Recalculate(kit, Catalog());

        Assert.Equal(14m, kit.CostPrice);
    }

    [Fact]
    public void Kit_sem_componentes_mantem_o_custo_informado()
    {
        var kit = new Kit { SuggestedPrice = 200m, CostPrice = 120m };

        KitPricing.Recalculate(kit, Catalog());

        Assert.Equal(120m, kit.CostPrice);
        Assert.Equal(40m, kit.MarginPercent);
    }

    [Theory]
    [InlineData(100, 0, 100)]
    [InlineData(100, 50, 50)]
    [InlineData(0, 100, 0)]
    public void Margem_sobre_o_preco_de_venda(decimal price, decimal cost, decimal expected) =>
        Assert.Equal(expected, KitPricing.Margin(price, cost));

    [Fact]
    public void Preco_para_atingir_a_margem_minima_arredonda_para_cima()
    {
        // custo 85 com margem de 30% => 85 / 0,70 = 121,4285...
        Assert.Equal(121.43m, KitPricing.PriceForMargin(85m, 30m));
        Assert.Equal(100m, KitPricing.PriceForMargin(70m, 30m));
    }
}
