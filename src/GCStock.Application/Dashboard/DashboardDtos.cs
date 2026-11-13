namespace GCStock.Application.Dashboard;

/// <summary>Faturamento de um período (vendas cobradas menos o devolvido) e o mesmo valor no período anterior equivalente.</summary>
public record PeriodRevenueDto(
    DateOnly From,
    DateOnly To,
    decimal Revenue,
    decimal PreviousRevenue,
    /// <summary>Variação % sobre o período anterior; nulo quando não há base de comparação.</summary>
    decimal? ChangePercent,
    int SalesCount,
    int PreviousSalesCount);

public record GoalDto(
    decimal Target,
    decimal Realized,
    decimal Remaining,
    decimal PercentAchieved,
    /// <summary>Estimativa de fechamento do mês no ritmo atual. É projeção, não valor realizado.</summary>
    decimal Projection,
    int DaysElapsed,
    int DaysInMonth);

public record RankedItemDto(string Name, string Unit, decimal Qty, decimal Revenue);

public record CategoryRevenueDto(string Category, decimal Revenue);

public record MonthPointDto(int Month, decimal NetRevenue, decimal NetResult);

public record CommercialDto(
    PeriodRevenueDto Day,
    PeriodRevenueDto Week,
    PeriodRevenueDto Month,
    GoalDto Goal,
    int KitsSold,
    decimal AverageTicket,
    decimal PreviousAverageTicket,
    List<RankedItemDto> TopProducts,
    List<RankedItemDto> TopKits,
    List<CategoryRevenueDto> ByCategory);

public record FinancialDashDto(
    DateOnly From,
    DateOnly To,
    decimal Revenue,
    decimal CostOfGoodsSold,
    decimal GrossProfit,
    decimal GrossMarginPercent,
    /// <summary>Despesas operacionais + folha + perdas + despesas financeiras do período.</summary>
    decimal TotalExpenses,
    decimal NetResult,
    decimal CashBalance,
    decimal PayablesOpen,
    decimal PayablesOverdue,
    decimal ReceivablesOpen,
    List<MonthPointDto> Evolution);

public record StockAlertItemDto(Guid ProductId, string Name, string Unit, decimal Stock, decimal MinStock, int DaysRemaining, decimal Qty);

public record TurnoverItemDto(string Name, string Unit, decimal SoldLast30Days, decimal Stock);

public record StockDashDto(
    int LowStockCount,
    List<StockAlertItemDto> LowStock,
    int OutOfStockCount,
    List<StockAlertItemDto> OutOfStock,
    int NearExpiryCount,
    List<StockAlertItemDto> NearExpiry,
    int ExpiredCount,
    /// <summary>Produtos com lote vencido e saldo: Qty = quantidade vencida.</summary>
    List<StockAlertItemDto> Expired,
    decimal StockValue,
    /// <summary>Mais vendidos nos últimos 30 dias (inclui o que saiu dentro de kits).</summary>
    List<TurnoverItemDto> TopTurnover,
    int StagnantCount,
    /// <summary>Com estoque e nenhuma venda nos últimos 30 dias, do maior valor parado para o menor.</summary>
    List<TurnoverItemDto> Stagnant);

public record KitRowDto(
    Guid Id,
    string Name,
    decimal CostPrice,
    decimal SuggestedPrice,
    decimal MarginPercent,
    decimal MinMarginPercent,
    bool IsBelowMinMargin,
    decimal PriceIncreaseNeeded,
    decimal TotalWeightKg,
    decimal Available,
    string StockStatus);

public record KitRankDto(string Name, decimal Qty, decimal Revenue, decimal Profit);

public record CostDriverDto(string Product, decimal Amount, decimal SharePercent);

public record KitDashDto(
    List<KitRowDto> Kits,
    int BelowMarginCount,
    KitRankDto? BestSeller,
    KitRankDto? MostProfitable,
    /// <summary>Produtos que mais pesam no custo dos kits (soma do custo dos componentes de todos os kits).</summary>
    List<CostDriverDto> CostDrivers);

/// <summary>Todos os indicadores do painel, calculados de dados reais (nenhum valor fictício).</summary>
public record DashboardDto(
    DateOnly Reference,
    CommercialDto Commercial,
    FinancialDashDto Financial,
    StockDashDto Stock,
    KitDashDto Kits);

public interface IDashboardService
{
    /// <summary>Indicadores do painel para a data de referência (padrão: hoje, horário de Brasília).</summary>
    Task<DashboardDto> GetAsync(DateOnly reference, CancellationToken ct);
}
