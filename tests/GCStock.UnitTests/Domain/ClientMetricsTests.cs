using GCStock.Domain.Services;

namespace GCStock.UnitTests.Domain;

public class ClientMetricsTests
{
    private static ClientMetrics.SaleFact Sale(int day, decimal total, decimal returned = 0m) =>
        new(new DateOnly(2026, 11, day), total, returned);

    [Fact]
    public void Cliente_sem_compras_tem_tudo_zerado()
    {
        var result = ClientMetrics.Compute([]);

        Assert.Equal((0m, 0, 0m), (result.TotalPurchased, result.PurchaseCount, result.AverageTicket));
        Assert.Null(result.FirstPurchase);
        Assert.Null(result.LastPurchase);
        Assert.Null(result.FrequencyDays);
    }

    [Fact]
    public void Total_comprado_ticket_medio_primeira_e_ultima_compra()
    {
        var result = ClientMetrics.Compute([Sale(10, 300m), Sale(2, 100m), Sale(20, 200m)]);

        Assert.Equal(600m, result.TotalPurchased);
        Assert.Equal(3, result.PurchaseCount);
        Assert.Equal(200m, result.AverageTicket);
        Assert.Equal(new DateOnly(2026, 11, 2), result.FirstPurchase);
        Assert.Equal(new DateOnly(2026, 11, 20), result.LastPurchase);
        // 18 dias entre a primeira e a última, 2 intervalos: 9 dias em média.
        Assert.Equal(9m, result.FrequencyDays);
    }

    [Fact]
    public void Devolucao_reduz_o_total_comprado_e_o_ticket()
    {
        var result = ClientMetrics.Compute([Sale(1, 300m, returned: 100m), Sale(5, 100m)]);

        Assert.Equal(300m, result.TotalPurchased);
        Assert.Equal(150m, result.AverageTicket);
    }

    [Fact]
    public void Uma_compra_so_nao_tem_frequencia() =>
        Assert.Null(ClientMetrics.Compute([Sale(3, 50m)]).FrequencyDays);

    [Theory]
    [InlineData(0, 0, 100, false)]       // sem limite definido: nunca bloqueia
    [InlineData(500, 0, 500, false)]     // exatamente no limite passa
    [InlineData(500, 400, 100, false)]
    [InlineData(500, 400, 100.01, true)]
    [InlineData(500, 600, 0.01, true)]   // já estourado: qualquer venda a prazo passa do limite
    public void Venda_a_prazo_acima_do_limite_e_detectada(double limit, double used, double amount, bool expected) =>
        Assert.Equal(expected, ClientMetrics.ExceedsLimit((decimal)limit, (decimal)used, (decimal)amount));

    [Theory]
    [InlineData(500, 120, 380)]
    [InlineData(500, 600, 0)]
    [InlineData(0, 100, 0)]
    public void Credito_disponivel_e_o_limite_menos_o_usado(double limit, double used, double expected) =>
        Assert.Equal((decimal)expected, ClientMetrics.CreditAvailable((decimal)limit, (decimal)used));
}
