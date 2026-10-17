namespace GCStock.Application.Finance;

public record AccountsReceivableDto(
    Guid Id,
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

/// <summary>Corrige um título em aberto. Título de venda (referência NFCe-) só muda o vencimento.</summary>
public record UpdateReceivableRequest(string? ClientOrOrigin, string? SubCategory, decimal? GrossAmount, DateOnly? DueDate);

public interface IReceivableService
{
    Task<AccountsReceivableDto?> UpdateAsync(Guid id, UpdateReceivableRequest request, CancellationToken ct);
    /// <summary>Apaga um título manual em aberto; títulos de venda se desfazem cancelando a venda.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
    Task<List<AccountsReceivableDto>> GetAllAsync(CancellationToken ct);
    Task<AccountsReceivableDto> AddAsync(AddReceivableRequest request, CancellationToken ct);
    Task<AccountsReceivableDto?> SettleAsync(Guid id, CancellationToken ct);
}
