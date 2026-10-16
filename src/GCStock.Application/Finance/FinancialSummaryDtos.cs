namespace GCStock.Application.Finance;

public record SupplierSpendDto(string Supplier, int Orders, decimal Amount);

/// <param name="Cost">Custo médio das mercadorias vendidas no período.</param>
/// <param name="HasEstimatedCost">true quando algum item é de venda antiga, com custo estimado pelo custo médio atual.</param>
public record ProductProfitDto(
    string Name,
    string Unit,
    decimal Qty,
    decimal Revenue,
    decimal Cost,
    decimal Profit,
    decimal MarginPercent,
    bool HasEstimatedCost);

public record ExpenseCategoryDto(string Category, decimal Amount);

public record LossReasonDto(string Reason, string Label, int Count, decimal Amount);

/// <summary>
/// Situação financeira de um período: o que foi gasto em entradas (compras), o que foi vendido, o custo das
/// mercadorias vendidas, o lucro bruto e o resultado depois das despesas e da folha.
/// </summary>
public record FinancialSummaryDto(
    DateOnly From,
    DateOnly To,
    // Entradas (compras)
    int PurchaseCount,
    decimal PurchasesTotal,
    decimal PurchasesPaid,
    decimal PurchasesPending,
    decimal PurchasesShipping,
    List<SupplierSpendDto> TopSuppliers,
    // Vendas
    int SalesCount,
    int CanceledSalesCount,
    decimal GrossRevenue,
    decimal Discounts,
    decimal Fees,
    decimal NetRevenue,
    // Resultado
    decimal CostOfGoodsSold,
    decimal GrossProfit,
    decimal GrossMarginPercent,
    decimal OperatingExpenses,
    decimal Payroll,
    // Perdas de estoque (vencimento, quebra...): prejuízo = quantidade × custo informado
    decimal Losses,
    int LossCount,
    List<LossReasonDto> LossesByReason,
    decimal NetResult,
    // Investimentos (equipamentos etc.): aparecem, mas não entram no resultado do período
    decimal CapitalExpenses,
    List<ExpenseCategoryDto> Expenses,
    // Estoque
    decimal StockValue,
    // Lucro por produto
    List<ProductProfitDto> ProductProfits,
    int EstimatedCostItems);

/// <summary>Um mês do balanço: o mesmo cálculo do resultado do período, mês a mês.</summary>
public record MonthlyBalanceDto(
    int Month,
    int SalesCount,
    decimal NetRevenue,
    decimal CostOfGoodsSold,
    decimal GrossProfit,
    decimal OperatingExpenses,
    decimal Payroll,
    decimal Losses,
    decimal NetResult,
    decimal PurchasesTotal,
    decimal CapitalExpenses);

/// <summary>
/// Lucro de um lote (uma compra de um produto). Receita = parte da receita líquida das vendas que saiu deste lote;
/// custo = quantidade vendida × custo do lote; perdas = prejuízo do que se perdeu dele.
/// </summary>
public record LotProfitDto(
    Guid LotId,
    string LotNumber,
    Guid ProductId,
    string ProductName,
    string Supplier,
    string Unit,
    DateOnly EntryDate,
    DateOnly ExpiryDate,
    decimal UnitCost,
    decimal QtyBought,
    decimal QtySold,
    decimal QtyLost,
    decimal QtyRemaining,
    decimal Revenue,
    decimal Cost,
    decimal LossCost,
    decimal Profit,
    decimal MarginPercent,
    decimal StockValue);

public interface IFinancialSummaryService
{
    Task<FinancialSummaryDto> GetAsync(DateOnly from, DateOnly to, CancellationToken ct);
    Task<List<MonthlyBalanceDto>> GetMonthlyAsync(int year, CancellationToken ct);
    Task<List<LotProfitDto>> GetLotProfitsAsync(DateOnly from, DateOnly to, CancellationToken ct);
}
