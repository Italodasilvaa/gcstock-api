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
    decimal NetResult,
    // Investimentos (equipamentos etc.): aparecem, mas não entram no resultado do período
    decimal CapitalExpenses,
    List<ExpenseCategoryDto> Expenses,
    // Estoque
    decimal StockValue,
    // Lucro por produto
    List<ProductProfitDto> ProductProfits,
    int EstimatedCostItems);

public interface IFinancialSummaryService
{
    Task<FinancialSummaryDto> GetAsync(DateOnly from, DateOnly to, CancellationToken ct);
}
