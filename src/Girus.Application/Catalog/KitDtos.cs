namespace Girus.Application.Catalog;

public record KitComponentDto(
    string ProductId,
    string Name,
    string Code,
    string LotNumber,
    decimal Quantity,
    string Unit,
    decimal UnitCost,
    decimal Subtotal,
    decimal AvailableStock);

public record KitDto(
    string Id,
    string Code,
    string Name,
    string Tag,
    string ServeLabel,
    string Description,
    decimal SuggestedPrice,
    decimal CostPrice,
    decimal MarginPercent,
    decimal TotalWeightKg,
    string ImageUrl,
    string ComponentsSummary,
    List<KitComponentDto> Components,
    decimal StockAvailable,
    string StockStatus,
    string BarcodeEan);

public record SaveKitRequest(
    string? Id,
    string? Name,
    string? Tag,
    string? ServeLabel,
    string? Description,
    decimal? SuggestedPrice,
    decimal? CostPrice,
    decimal? MarginPercent,
    decimal? TotalWeightKg,
    string? ImageUrl,
    string? ComponentsSummary,
    List<KitComponentDto>? Components,
    decimal? StockAvailable);

public interface IKitService
{
    Task<List<KitDto>> GetAllAsync(CancellationToken ct);
    Task<KitDto> SaveAsync(SaveKitRequest request, CancellationToken ct);

    /// <summary>Exclui o kit definitivamente — nunca acontece automaticamente (ex.: ao vender), só por ação explícita do usuário.</summary>
    Task DeleteAsync(string id, CancellationToken ct);
}
