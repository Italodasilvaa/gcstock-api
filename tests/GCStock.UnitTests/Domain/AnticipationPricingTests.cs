using GCStock.Domain.Services;

namespace GCStock.UnitTests.Domain;

public class AnticipationPricingTests
{
    [Theory]
    [InlineData(100, 2.5, 2.50)]
    [InlineData(96.10, 2.5, 2.40)]   // 2,4025 => 2,40
    [InlineData(33.33, 3, 1.00)]     // 0,9999 => 1,00
    [InlineData(0, 2.5, 0)]
    [InlineData(250, 0, 0)]
    public void Custo_da_antecipacao_e_o_percentual_do_liquido_arredondado_ao_centavo(double net, double percent, double expected) =>
        Assert.Equal((decimal)expected, SalePricing.AnticipationFee((decimal)net, (decimal)percent));

    [Theory]
    [InlineData(-0.1)]
    [InlineData(100)]
    public void Percentual_fora_do_intervalo_e_recusado(double percent) =>
        Assert.Throws<ArgumentException>(() => SalePricing.AnticipationFee(100m, (decimal)percent));
}
