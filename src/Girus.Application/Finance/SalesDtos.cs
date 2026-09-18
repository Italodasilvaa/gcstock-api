namespace Girus.Application.Finance;

public record SaleRecordDto(
    string Id,
    string SaleId,
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
