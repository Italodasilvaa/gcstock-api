namespace Girus.Application.Finance;

public record AccountsReceivableDto(
    string Id,
    string ClientOrOrigin,
    string SubCategory,
    string Reference,
    DateOnly DueDate,
    string DueLabel,
    decimal GrossAmount,
    decimal FeeRetention,
    decimal NetAmount,
    string Status,
    string Type,
    string PaymentModeDesc,
    DateOnly? ReceivedDate);

public record AddReceivableRequest(
    string? ClientOrOrigin,
    string? SubCategory,
    string? Reference,
    DateOnly? DueDate,
    string? DueLabel,
    decimal? GrossAmount,
    decimal? FeeRetention,
    string? Status,
    string? Type,
    string? PaymentModeDesc);

public interface IReceivableService
{
    Task<List<AccountsReceivableDto>> GetAllAsync(CancellationToken ct);
    Task<AccountsReceivableDto> AddAsync(AddReceivableRequest request, CancellationToken ct);
    Task<AccountsReceivableDto?> SettleAsync(string id, CancellationToken ct);
}
