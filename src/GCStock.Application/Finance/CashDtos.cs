namespace GCStock.Application.Finance;

public record CapitalContributionDto(Guid Id, DateOnly Date, string Description, string Source, decimal Amount, string CreatedBy);

public record AddCapitalContributionRequest(string Description, string? Source, decimal Amount, DateOnly? Date);

/// <summary>Uma linha do extrato do caixa: Amount é positivo (entrou) ou negativo (saiu).</summary>
/// <param name="Kind">"aporte" | "venda" | "recebimento" | "compra" | "despesa".</param>
public record CashMovementDto(DateOnly Date, string Kind, string Description, decimal Amount);

/// <summary>
/// Saúde financeira pelo regime de caixa: só conta o dinheiro que realmente entrou ou saiu. Entra: aportes, vendas
/// recebidas na hora (dinheiro, Pix, débito) e títulos a receber liquidados (cartão, fiado). Sai: contas a pagar pagas
/// (compras de mercadoria, equipamentos e despesas). A folha de pagamento não é lançada como paga, então não sai do caixa.
/// </summary>
public record CashSummaryDto(
    DateOnly From,
    DateOnly To,
    decimal OpeningBalance,
    decimal Contributions,
    decimal SalesReceived,
    decimal ReceivablesReceived,
    decimal TotalIn,
    decimal PurchasesPaid,
    decimal EquipmentPaid,
    decimal ExpensesPaid,
    decimal TotalOut,
    decimal ClosingBalance,
    // Foto de hoje (independe do período escolhido)
    decimal CurrentBalance,
    decimal ToReceive,
    decimal ToPay,
    decimal OverduePayable,
    decimal StockValue,
    decimal MonthlyPayroll,
    List<CashMovementDto> Movements);

public interface ICashService
{
    Task<CashSummaryDto> GetAsync(DateOnly from, DateOnly to, CancellationToken ct);
    Task<List<CapitalContributionDto>> GetContributionsAsync(CancellationToken ct);
    Task<CapitalContributionDto> AddContributionAsync(AddCapitalContributionRequest request, CancellationToken ct);
    Task<bool> DeleteContributionAsync(Guid id, CancellationToken ct);
}
