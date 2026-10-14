namespace GCStock.Application.Common;

/// <summary>Rótulos relativos em pt-BR para datas de vencimento (ex: "Vence hoje", "Em 5 dias", "Vencido há 2 dias").</summary>
public static class DateLabelHelper
{
    public static string DueLabelFor(DateOnly dueDate)
    {
        var diffDays = dueDate.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;

        if (diffDays < 0)
        {
            var abs = Math.Abs(diffDays);
            return $"Vencido há {abs} dia{(abs == 1 ? string.Empty : "s")}";
        }

        if (diffDays == 0) return "Vence hoje";
        if (diffDays == 1) return "Amanhã";
        return $"Em {diffDays} dias";
    }
}
