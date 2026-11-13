namespace GCStock.Domain.Services;

/// <summary>
/// Indicadores do cliente calculados das vendas de verdade (nunca de um total guardado que fica desatualizado) e a regra do
/// limite de crédito. "Comprado" não é "devedor": o saldo em aberto vem dos títulos a receber.
/// </summary>
public static class ClientMetrics
{
    /// <summary>Uma venda do cliente: dia (Brasília), valor cobrado e quanto dele já foi devolvido.</summary>
    public sealed record SaleFact(DateOnly Date, decimal Total, decimal Returned);

    public sealed record Result(
        decimal TotalPurchased,
        int PurchaseCount,
        DateOnly? FirstPurchase,
        DateOnly? LastPurchase,
        decimal AverageTicket,
        /// <summary>Intervalo médio, em dias, entre compras consecutivas; nulo com menos de 2 compras.</summary>
        decimal? FrequencyDays);

    /// <summary>Vendas canceladas não entram. Total comprado = soma do cobrado menos o devolvido.</summary>
    public static Result Compute(IEnumerable<SaleFact> sales)
    {
        var list = sales.OrderBy(s => s.Date).ToList();
        if (list.Count == 0) return new Result(0m, 0, null, null, 0m, null);

        var total = Math.Max(0m, list.Sum(s => s.Total - s.Returned));
        decimal? frequency = null;
        if (list.Count >= 2)
        {
            var span = list[^1].Date.DayNumber - list[0].Date.DayNumber;
            frequency = Math.Round((decimal)span / (list.Count - 1), 1, MidpointRounding.AwayFromZero);
        }

        return new Result(
            Math.Round(total, 2, MidpointRounding.AwayFromZero),
            list.Count,
            list[0].Date,
            list[^1].Date,
            Math.Round(total / list.Count, 2, MidpointRounding.AwayFromZero),
            frequency);
    }

    /// <summary>Limite de crédito menor ou igual a zero = sem limite definido (não bloqueia a venda a prazo).</summary>
    public static bool HasLimit(decimal creditLimit) => creditLimit > 0m;

    /// <summary>Quanto ainda pode ser vendido a prazo: limite menos o que já está em aberto (0 sem limite definido ou estourado).</summary>
    public static decimal CreditAvailable(decimal creditLimit, decimal used) =>
        HasLimit(creditLimit) ? Math.Max(0m, creditLimit - used) : 0m;

    /// <summary>A nova venda a prazo faz o saldo em aberto passar do limite?</summary>
    public static bool ExceedsLimit(decimal creditLimit, decimal used, decimal newAmount) =>
        HasLimit(creditLimit) && used + newAmount > creditLimit + 0.005m;
}
