namespace GCStock.Domain.Services;

/// <summary>
/// Regras de validade num só lugar. O status de lote e de produto é sempre derivado da data de vencimento e do dia de
/// hoje (horário de Brasília), nunca lido de um valor gravado que envelhece com o passar dos dias.
/// </summary>
public static class ExpiryRules
{
    /// <summary>Hoje no horário de Brasília (UTC-3), o dia que vale para vencimento.</summary>
    public static DateOnly Today(DateTime utcNow) => DateOnly.FromDateTime(utcNow.AddHours(-3));

    public static int DaysRemaining(DateOnly expiry, DateOnly today) => expiry.DayNumber - today.DayNumber;

    /// <summary>Lote: "vencido" | "urgente" (dentro dos dias de alerta do produto) | "regular" (até 15 dias) | "novo".</summary>
    public static string LotStatus(int daysRemaining, int alertDays) =>
        daysRemaining < 0 ? "vencido" : daysRemaining <= alertDays ? "urgente" : daysRemaining <= 15 ? "regular" : "novo";

    /// <summary>Produto: "vencido" | "critico" (dentro dos dias de alerta) | "normal" (até 30 dias) | "longo".</summary>
    public static string ProductStatus(int daysRemaining, int alertDays) =>
        daysRemaining < 0 ? "vencido" : daysRemaining <= alertDays ? "critico" : daysRemaining <= 30 ? "normal" : "longo";

    /// <summary>Lote vencido não pode ser vendido: vale a partir do dia seguinte ao vencimento.</summary>
    public static bool IsExpired(DateOnly expiry, DateOnly today) => expiry < today;
}
