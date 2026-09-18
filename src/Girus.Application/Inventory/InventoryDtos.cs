namespace Girus.Application.Inventory;

public record StockBatchDto(
    string Id,
    string ProductId,
    string ProductName,
    string Sku,
    string LotNumber,
    decimal AvailableKg,
    DateOnly EntryDate,
    DateOnly ExpiryDate,
    int DaysRemaining,
    string FiscalDoc,
    string Supplier,
    string Status);

public record StockMovementDto(
    string Id,
    DateTime Timestamp,
    string Type,
    string Title,
    string DocInfo,
    decimal AmountKg,
    string Description,
    string BadgeLabel,
    string AuditLink);

public interface IInventoryService
{
    Task<List<StockBatchDto>> GetBatchesAsync(CancellationToken ct);
    Task<List<StockMovementDto>> GetMovementsAsync(CancellationToken ct);
}
