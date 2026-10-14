namespace GCStock.Application.Catalog;

public record ProductDto(
    Guid Id,
    string Sku,
    string Code,
    string Ean,
    string Name,
    string Category,
    string Unit,
    decimal Stock,
    decimal MinStock,
    decimal CostPrice,
    decimal SellingPrice,
    bool IsPerishable,
    int AlertDays,
    string StorageTemp,
    DateOnly NearestExpiry,
    int ExpiryDaysRemaining,
    string ExpiryStatus,
    string LotNumber,
    string Supplier,
    string ImageUrl,
    string? SubLocation,
    bool? IsPromo);

public record SaveProductRequest(
    Guid? Id,
    string? Sku,
    string? Ean,
    string? Name,
    string? Category,
    string? Unit,
    decimal? Stock,
    decimal? MinStock,
    decimal? CostPrice,
    decimal? SellingPrice,
    bool? IsPerishable,
    int? AlertDays,
    string? StorageTemp,
    DateOnly? NearestExpiry,
    int? ExpiryDaysRemaining,
    string? ExpiryStatus,
    string? LotNumber,
    string? Supplier,
    string? ImageUrl,
    string? SubLocation,
    bool? IsPromo);

public interface IProductService
{
    Task<List<ProductDto>> GetAllAsync(CancellationToken ct);
    Task<ProductDto> SaveAsync(SaveProductRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}
