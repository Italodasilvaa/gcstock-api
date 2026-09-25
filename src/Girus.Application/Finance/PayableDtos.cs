namespace Girus.Application.Finance;

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

public interface IPayableService
{
    Task<List<AccountsPayableDto>> GetAllAsync(CancellationToken ct);
    Task<AccountsPayableDto> AddAsync(AddPayableRequest request, CancellationToken ct);
    Task<AccountsPayableDto?> SettleAsync(Guid id, SettlePayableRequest request, CancellationToken ct);
}
