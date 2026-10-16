namespace GCStock.Application.Inventory;

public record StockBatchDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string Sku,
    string LotNumber,
    decimal AvailableKg,
    DateOnly EntryDate,
    DateOnly ExpiryDate,
    int DaysRemaining,
    string FiscalDoc,
    string Supplier,
    string Status,
    string Unit,
    decimal UnitCost = 0,
    /// <summary>Número do pedido de compra que originou o lote (vazio no estoque sem lote).</summary>
    string OrderCode = "");

public record StockMovementDto(
    Guid Id,
    DateTime Timestamp,
    string Type,
    string Title,
    string DocInfo,
    decimal AmountKg,
    string Description,
    string BadgeLabel,
    string AuditLink,
    string Unit);

public interface IInventoryService
{
    Task<List<StockBatchDto>> GetBatchesAsync(CancellationToken ct);
    Task<List<StockMovementDto>> GetMovementsAsync(CancellationToken ct);
}
