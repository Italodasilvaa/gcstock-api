using GCStock.Domain.Entities;
using GCStock.Domain.Services;

namespace GCStock.UnitTests.Domain;

public class ExpiryAndAvailabilityTests
{
    private static readonly DateOnly Today = new(2026, 11, 12);

    [Theory]
    [InlineData(-1, 3, "vencido")]
    [InlineData(0, 3, "urgente")]
    [InlineData(3, 3, "urgente")]
    [InlineData(4, 3, "regular")]
    [InlineData(15, 3, "regular")]
    [InlineData(16, 3, "novo")]
    public void Status_do_lote_vem_dos_dias_restantes(int days, int alertDays, string expected) =>
        Assert.Equal(expected, ExpiryRules.LotStatus(days, alertDays));

    [Theory]
    [InlineData(-1, 3, "vencido")]
    [InlineData(2, 3, "critico")]
    [InlineData(30, 3, "normal")]
    [InlineData(31, 3, "longo")]
    public void Status_do_produto_vem_dos_dias_restantes(int days, int alertDays, string expected) =>
        Assert.Equal(expected, ExpiryRules.ProductStatus(days, alertDays));

    [Fact]
    public void Lote_vence_no_dia_seguinte_ao_vencimento_nao_no_proprio_dia()
    {
        Assert.False(ExpiryRules.IsExpired(Today, Today));
        Assert.True(ExpiryRules.IsExpired(Today.AddDays(-1), Today));
        Assert.Equal(5, ExpiryRules.DaysRemaining(Today.AddDays(5), Today));
    }

    [Fact]
    public void Hoje_usa_o_horario_de_brasilia()
    {
        // 01:00 UTC ainda é o dia anterior no Brasil (UTC-3).
        Assert.Equal(new DateOnly(2026, 11, 11), ExpiryRules.Today(new DateTime(2026, 11, 12, 1, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateOnly(2026, 11, 12), ExpiryRules.Today(new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc)));
    }

    private static Product Product(decimal stock) => new() { Name = "P", Stock = stock };

    private static Kit KitOf(params (Product Product, decimal Qty)[] parts) => new()
    {
        Components = parts.Select(p => new KitComponent { ProductId = p.Product.Id, Quantity = p.Qty }).ToList(),
    };

    private static Dictionary<Guid, Product> Catalog(params Product[] products) => products.ToDictionary(p => p.Id);

    [Fact]
    public void Kits_disponiveis_e_limitado_pelo_componente_mais_escasso()
    {
        var picanha = Product(10m);   // 10 kg / 1 kg  = 10 kits
        var linguica = Product(2m);   // 2 kg / 0,8 kg = 2,5 => 2 kits
        var kit = KitOf((picanha, 1m), (linguica, 0.8m));

        Assert.Equal(2m, KitPricing.MaxKits(kit, Catalog(picanha, linguica)));
    }

    [Fact]
    public void Sem_estoque_de_um_componente_nao_ha_kit()
    {
        var picanha = Product(10m);
        var carvao = Product(0m);

        Assert.Equal(0m, KitPricing.MaxKits(KitOf((picanha, 1m), (carvao, 1m)), Catalog(picanha, carvao)));
    }

    [Fact]
    public void Estoque_negativo_conta_como_zero_e_produto_removido_zera_a_disponibilidade()
    {
        var negative = Product(-5m);
        var gone = Product(100m);

        Assert.Equal(0m, KitPricing.MaxKits(KitOf((negative, 1m)), Catalog(negative)));
        Assert.Equal(0m, KitPricing.MaxKits(KitOf((gone, 1m)), Catalog()));
    }

    [Fact]
    public void Kit_sem_componentes_nao_pode_ser_montado() =>
        Assert.Equal(0m, KitPricing.MaxKits(new Kit(), Catalog()));

    [Theory]
    [InlineData(0, "alerta")]
    [InlineData(2, "alerta")]
    [InlineData(3, "disponivel")]
    [InlineData(9, "disponivel")]
    [InlineData(10, "amplo")]
    public void Faixa_de_disponibilidade_do_kit(int kits, string expected) =>
        Assert.Equal(expected, KitPricing.StockStatus(kits));
}
