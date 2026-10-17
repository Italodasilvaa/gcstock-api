namespace GCStock.Application.Finance;

public record AccountsPayableDto(
    Guid Id,
    string Supplier,
    string Description,
    string NfeReference,
    DateOnly IssueDate,
    DateOnly DueDate,
    string DueLabel,
    decimal Amount,
    string PaymentMethod,
    string Origin,
    string Status,
    DateOnly? PaidDate,
    bool? ReceiptAttached,
    string Category);

public record AddPayableRequest(
    string? Supplier,
    string? Description,
    string? NfeReference,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    string? DueLabel,
    decimal? Amount,
    string? PaymentMethod,
    string? Origin,
    string? Category);

public record SettlePayableRequest(string BankAccount, string PaymentMode);

/// <summary>Corrige uma despesa lançada: só despesas/equipamentos (compras de mercadoria se editam em Compras).</summary>
public record UpdatePayableRequest(string? Supplier, string? Description, decimal? Amount, DateOnly? DueDate, DateOnly? IssueDate, string? Category, DateOnly? PaidDate = null);

/// <param name="Category">"equipamento" (máquina/item comprado com o capital: não pesa no resultado) ou "outros" (despesa do dia a dia).</param>
public record ChangePayableCategoryRequest(string Category);

public interface IPayableService
{
    /// <summary>Apaga uma despesa/equipamento lançado por engano. Compras de mercadoria não podem ser apagadas por aqui.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
    Task<AccountsPayableDto?> UpdateAsync(Guid id, UpdatePayableRequest request, CancellationToken ct);
    Task<AccountsPayableDto?> ChangeCategoryAsync(Guid id, ChangePayableCategoryRequest request, CancellationToken ct);
    Task<List<AccountsPayableDto>> GetAllAsync(CancellationToken ct);
    Task<AccountsPayableDto> AddAsync(AddPayableRequest request, CancellationToken ct);
    Task<AccountsPayableDto?> SettleAsync(Guid id, SettlePayableRequest request, CancellationToken ct);
}
