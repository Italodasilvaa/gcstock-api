using GCStock.Domain.Services;

namespace GCStock.UnitTests.Domain;

public class SalePricingTests
{
    [Fact]
    public void Taxa_repassada_ao_cliente_cobra_base_mais_taxa_e_o_lojista_recebe_a_base()
    {
        // R$ 100 no cartão a 3,5% + R$ 0,40 fixos, taxa repassada.
        var result = SalePricing.Calculate(100m, 0m, 3.5m, 0.40m, passFeeToCustomer: true);

        Assert.Equal(3.90m, result.Fee);
        Assert.Equal(103.90m, result.Total);
        Assert.Equal(100m, result.Net);
    }

    [Fact]
    public void Taxa_absorvida_pelo_lojista_cobra_a_base_e_desconta_a_taxa_do_liquido()
    {
        var result = SalePricing.Calculate(100m, 0m, 3.5m, 0.40m, passFeeToCustomer: false);

        Assert.Equal(100m, result.Total);
        Assert.Equal(96.10m, result.Net);
        Assert.Equal(3.90m, result.Fee);
    }

    [Fact]
    public void Desconto_reduz_a_base_antes_da_taxa()
    {
        // Subtotal 200, desconto 20 => base 180; taxa 2% = 3,60.
        var result = SalePricing.Calculate(200m, 20m, 2m, 0m, passFeeToCustomer: true);

        Assert.Equal(180m, result.BaseAmount);
        Assert.Equal(3.60m, result.Fee);
        Assert.Equal(183.60m, result.Total);
        Assert.Equal(180m, result.Net);
        Assert.Equal(20m, result.Discount);
    }

    [Fact]
    public void Venda_zerada_por_desconto_nao_cobra_taxa()
    {
        var result = SalePricing.Calculate(50m, 50m, 5m, 1m, passFeeToCustomer: true);

        Assert.Equal(0m, result.Fee);
        Assert.Equal(0m, result.Total);
    }

    [Fact]
    public void Dinheiro_e_pix_sem_taxa_cobram_o_valor_dos_itens()
    {
        var result = SalePricing.Calculate(87.65m, 0m, 0m, 0m, passFeeToCustomer: true);

        Assert.Equal(87.65m, result.Total);
        Assert.Equal(87.65m, result.Net);
    }

    [Fact]
    public void Taxa_e_arredondada_ao_centavo()
    {
        // 33,33 x 2,99% = 0,996567 => 1,00
        Assert.Equal(1.00m, SalePricing.Calculate(33.33m, 0m, 2.99m, 0m, true).Fee);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    public void Desconto_negativo_ou_maior_que_o_total_e_recusado(double discount) =>
        Assert.Throws<ArgumentException>(() => SalePricing.Calculate(100m, (decimal)discount, 0m, 0m, true));

    [Theory]
    [InlineData("Pix", "pix")]
    [InlineData("Dinheiro (Espécie)", "dinheiro")]
    [InlineData("Cartão de Débito", "debito")]
    [InlineData("Cartão de Crédito Parcelado", "parcelado")]
    [InlineData("Cartão de Crédito à vista", "credito_vista")]
    [InlineData("Fiado / A Prazo", "a_prazo")]
    [InlineData("Boleto", "a_prazo")]
    [InlineData("Qualquer outra", "dinheiro")]
    public void Forma_de_pagamento_e_reconhecida_pelo_nome_cadastrado(string method, string expected) =>
        Assert.Equal(expected, SalePricing.InferMode(method));

    [Theory]
    [InlineData(100, 3)]
    [InlineData(100.01, 3)]
    [InlineData(0.05, 3)]
    [InlineData(99.99, 12)]
    public void Parcelas_somam_exatamente_o_total(double total, int count)
    {
        var parts = SalePricing.SplitInstallments((decimal)total, count);

        Assert.Equal(count, parts.Count);
        Assert.Equal((decimal)total, parts.Sum());
        // Só a última parcela absorve a diferença de centavos, e nunca fica menor que as demais.
        Assert.All(parts.Take(count - 1), p => Assert.Equal(parts[0], p));
        Assert.True(parts[^1] >= parts[0]);
    }

    [Fact]
    public void Uma_parcela_e_o_proprio_valor() =>
        Assert.Equal([123.45m], SalePricing.SplitInstallments(123.45m, 1));

    [Fact]
    public void Zero_parcelas_e_recusado() =>
        Assert.Throws<ArgumentException>(() => SalePricing.SplitInstallments(10m, 0));
}
