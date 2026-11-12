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

/// <summary>Uma mudança do custo médio do produto. Source: "compra" | "edicao_compra" | "cancelamento_compra" | "ajuste_manual".</summary>
public record ProductCostHistoryDto(
    Guid Id, decimal PreviousCost, decimal NewCost, string Source, string? PurchaseCode, DateTime CreatedAt, string CreatedBy);

public interface IProductService
{
    Task<List<ProductDto>> GetAllAsync(CancellationToken ct);

    /// <summary>Histórico de custo médio do produto, do mais recente para o mais antigo. O das vendas já feitas não muda.</summary>
    Task<List<ProductCostHistoryDto>> GetCostHistoryAsync(Guid productId, CancellationToken ct);
    Task<ProductDto> SaveAsync(SaveProductRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}
