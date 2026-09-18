namespace Girus.Application.Purchasing;

public record PurchaseItemDto(
    string Name,
    string Code,
    decimal Quantity,
    string Unit,
    decimal UnitCost,
    decimal Subtotal,
    DateOnly ExpiryDate,
    string LotNumber,
    bool IsPerishable);

public record PurchaseEntryDto(
    string Id,
    string OrderCode,
    string NfeNumber,
    string NfeSeries,
    DateOnly EntryDate,
    string EntryTime,
    string SupplierName,
    string SupplierCnpj,
    string SupplierCity,
    string SupplierSif,
    int ItemsCount,
    decimal TotalWeightKg,
    decimal TotalAmount,
    decimal ShippingCost,
    string PaymentCondition,
    string PaymentStatus,
    string PaymentMode,
    List<PurchaseItemDto> Items);

public record SavePurchaseRequest(
    string? NfeNumber,
    string? NfeSeries,
    DateOnly? EntryDate,
    string? SupplierName,
    string? SupplierCnpj,
    string? SupplierCity,
    string? SupplierSif,
    int? ItemsCount,
    decimal? TotalWeightKg,
    decimal? TotalAmount,
    decimal? ShippingCost,
    string? PaymentCondition,
    string? PaymentMode,
    List<PurchaseItemDto>? Items);

public interface IPurchaseService
{
    Task<List<PurchaseEntryDto>> GetAllAsync(CancellationToken ct);

    /// <summary>Registra a entrada e cria automaticamente um título em Contas a Pagar (Módulo Compras).</summary>
    Task<PurchaseEntryDto> SaveAsync(SavePurchaseRequest request, CancellationToken ct);
}
