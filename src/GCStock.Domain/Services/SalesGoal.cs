namespace GCStock.Domain.Services;

/// <summary>Meta mensal de faturamento, semana e comparação com o período anterior (regras puras, sem banco).</summary>
public static class SalesGoal
{
    public sealed record Progress(
        decimal Target, decimal Realized, decimal Remaining, decimal PercentAchieved,
        /// <summary>Estimativa de fechamento do mês no ritmo atual (mês corrente) — não é valor realizado.</summary>
        decimal Projection, int DaysElapsed, int DaysInMonth);

    /// <summary>
    /// Realizado x meta do mês de <paramref name="reference"/>. A projeção só extrapola o mês corrente (realizado ÷ dias passados x dias do
    /// mês); mês passado projeta o próprio realizado e mês futuro, zero.
    /// </summary>
    public static Progress Compute(decimal target, decimal realized, DateOnly reference, DateOnly today)
    {
        var daysInMonth = DateTime.DaysInMonth(reference.Year, reference.Month);
        var sameMonthAsToday = reference.Year == today.Year && reference.Month == today.Month;
        var isPast = reference.Year < today.Year || (reference.Year == today.Year && reference.Month < today.Month);
        var elapsed = sameMonthAsToday ? today.Day : isPast ? daysInMonth : 0;

        var projection = elapsed == 0 ? 0m : isPast ? realized : Math.Round(realized / elapsed * daysInMonth, 2, MidpointRounding.AwayFromZero);
        var percent = target > 0 ? Math.Round(realized / target * 100m, 1, MidpointRounding.AwayFromZero) : 0m;
        return new Progress(target, realized, Math.Max(0m, target - realized), percent, projection, elapsed, daysInMonth);
    }

    /// <summary>Semana de segunda a domingo que contém a data.</summary>
    public static (DateOnly From, DateOnly To) WeekOf(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7; // segunda = 0
        var from = date.AddDays(-offset);
        return (from, from.AddDays(6));
    }

    /// <summary>Variação % sobre o período anterior; nulo quando não há base de comparação (anterior zerado e atual positivo).</summary>
    public static decimal? ChangePercent(decimal current, decimal previous)
    {
        if (previous == 0m) return current == 0m ? 0m : null;
        return Math.Round((current - previous) / previous * 100m, 1, MidpointRounding.AwayFromZero);
    }
}
