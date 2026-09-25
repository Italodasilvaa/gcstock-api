namespace Girus.Application.Finance;

public record SaleRecordDto(
    Guid Id,
    Guid SaleId,
    DateTime Timestamp,
    decimal Total,
    decimal NetAmount,
    decimal FeeAmount,
    string PaymentMode,
    string? ClientName,
    string SoldBy);

public interface ISalesHistoryService
{
    Task<List<SaleRecordDto>> GetAllAsync(CancellationToken ct);
}
