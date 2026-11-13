namespace GCStock.Application.Catalog;

public record KitComponentDto(
    Guid ProductId,
    string Name,
    string Code,
    string LotNumber,
    decimal Quantity,
    string Unit,
    decimal UnitCost,
    decimal Subtotal,
    decimal AvailableStock);

public record KitDto(
    Guid Id,
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
    string BarcodeEan,
    /// <summary>Margem mínima efetiva (a do kit, ou a padrão da empresa quando o kit não define a sua).</summary>
    decimal MinMarginPercent = 30m,
    /// <summary>true quando o kit não tem margem própria e usa a da empresa.</summary>
    bool UsesCompanyMinMargin = true,
    bool IsBelowMinMargin = false,
    /// <summary>Menor preço de venda que entrega a margem mínima sobre o custo atual.</summary>
    decimal PriceForMinMargin = 0m,
    /// <summary>Quanto o preço atual precisaria subir para atingir a margem mínima (0 quando já atinge).</summary>
    decimal PriceIncreaseNeeded = 0m);

/// <summary>Uma mudança de custo/margem de um kit. Source: "recalculo_automatico" | "edicao_kit".</summary>
public record KitCostHistoryDto(
    Guid Id,
    decimal PreviousCost,
    decimal NewCost,
    decimal PreviousMarginPercent,
    decimal NewMarginPercent,
    decimal SuggestedPrice,
    string Source,
    string? TriggerProduct,
    DateTime CreatedAt,
    string CreatedBy);

public record SaveKitRequest(
    Guid? Id,
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
    decimal? StockAvailable,
    /// <summary>Margem mínima própria do kit; nulo = usar a da empresa. Custo e margem NÃO vêm do cliente: o servidor calcula.</summary>
    decimal? MinMarginPercent = null);

public interface IKitService
{
    Task<List<KitDto>> GetAllAsync(CancellationToken ct);
    Task<KitDto> SaveAsync(SaveKitRequest request, CancellationToken ct);

    /// <summary>Histórico de custo e margem do kit, do mais recente para o mais antigo.</summary>
    Task<List<KitCostHistoryDto>> GetCostHistoryAsync(Guid kitId, CancellationToken ct);

    /// <summary>Exclui o kit definitivamente — nunca acontece automaticamente (ex.: ao vender), só por ação explícita do usuário.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct);
}
