namespace Girus.Application.Finance;

public record AccountsPayableDto(
    string Id,
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
    bool? ReceiptAttached);

public record AddPayableRequest(
    string? Supplier,
    string? Description,
    string? NfeReference,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    string? DueLabel,
    decimal? Amount,
    string? PaymentMethod,
    string? Origin);

public record SettlePayableRequest(string BankAccount, string PaymentMode);

public interface IPayableService
{
    Task<List<AccountsPayableDto>> GetAllAsync(CancellationToken ct);
    Task<AccountsPayableDto> AddAsync(AddPayableRequest request, CancellationToken ct);
    Task<AccountsPayableDto?> SettleAsync(string id, SettlePayableRequest request, CancellationToken ct);
}
