using System.Globalization;
using System.Text;

namespace GCStock.Domain.Services;

/// <summary>
/// Conta da venda no servidor: desconto, taxa de pagamento, total cobrado, valor líquido e parcelas. A tela só simula;
/// o que vale é o resultado daqui, calculado a partir dos itens e da forma de pagamento cadastrada.
/// </summary>
public static class SalePricing
{
    /// <summary>Formas de pagamento reconhecidas pelo PDV e pelo caixa.</summary>
    public static readonly string[] Modes = ["pix", "dinheiro", "debito", "credito_vista", "parcelado", "a_prazo"];

    public const int MaxInstallments = 12;

    public sealed record Result(
        decimal Subtotal, decimal Discount, decimal BaseAmount, decimal Fee, decimal Total, decimal Net);

    /// <summary>
    /// Base = subtotal - desconto. Taxa = base x % + taxa fixa (zero se a base é zero). Taxa repassada: o cliente paga base + taxa
    /// e o lojista recebe a base; não repassada: o cliente paga a base e o lojista recebe base - taxa.
    /// </summary>
    public static Result Calculate(decimal subtotal, decimal discount, decimal feePercent, decimal fixedFee, bool passFeeToCustomer)
    {
        if (discount < 0) throw new ArgumentException("O desconto não pode ser negativo.");
        if (discount > subtotal) throw new ArgumentException("O desconto não pode ser maior que o total dos itens.");

        var baseAmount = Round(subtotal - discount);
        var fee = baseAmount > 0 ? Round(baseAmount * feePercent / 100m + fixedFee) : 0m;
        var total = passFeeToCustomer ? Round(baseAmount + fee) : baseAmount;
        var net = passFeeToCustomer ? baseAmount : Round(baseAmount - fee);
        return new Result(Round(subtotal), Round(discount), baseAmount, fee, total, net);
    }

    /// <summary>Custo de antecipar um recebível: percentual sobre o valor líquido da parcela, arredondado ao centavo.</summary>
    public static decimal AnticipationFee(decimal netAmount, decimal percent)
    {
        if (percent < 0 || percent >= 100) throw new ArgumentException("A taxa de antecipação deve ficar entre 0 e 99,99%.");
        return Round(netAmount * percent / 100m);
    }

    /// <summary>Forma de pagamento a partir do nome cadastrado em Configurações (mesma regra que a tela sempre usou).</summary>
    public static string InferMode(string method)
    {
        var name = RemoveDiacritics(method).ToLowerInvariant();
        if (name.Contains("pix")) return "pix";
        if (name.Contains("dinheiro") || name.Contains("especie")) return "dinheiro";
        if (name.Contains("debito")) return "debito";
        if (name.Contains("parcel")) return "parcelado";
        if (name.Contains("credito")) return "credito_vista";
        if (name.Contains("prazo") || name.Contains("fiado") || name.Contains("boleto")) return "a_prazo";
        return "dinheiro";
    }

    /// <summary>Divide o valor em N parcelas com centavos exatos: as parcelas somam o total e a diferença de arredondamento vai para a última.</summary>
    public static List<decimal> SplitInstallments(decimal total, int count)
    {
        if (count < 1) throw new ArgumentException("Informe ao menos 1 parcela.");

        var each = Math.Floor(total / count * 100m) / 100m;
        var parts = Enumerable.Repeat(each, count).ToList();
        parts[^1] = Round(total - each * (count - 1));
        return parts;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
