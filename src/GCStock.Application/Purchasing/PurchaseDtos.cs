namespace GCStock.Application.Purchasing;

public record PurchaseItemDto(
    string Name,
    string Code,
    decimal Quantity,
    string Unit,
    decimal UnitCost,
    decimal Subtotal,
    DateOnly? ExpiryDate,
    string LotNumber,
    bool IsPerishable,
    Guid? Id = null,
    // Id do produto no catálogo — quando informado, a entrada soma automaticamente ao estoque dele.
    Guid? ProductId = null);

public record PurchaseEntryDto(
    Guid Id,
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
    List<PurchaseItemDto> Items,
    bool IsDeleted = false,
    DateTime? CancelledAt = null,
    string? CancelledBy = null,
    string? CancelReason = null,
    // "nao_aplicavel" | "pendente" | "parcial" | "devolvido" - só existe para compra paga e cancelada.
    string RefundStatus = "nao_aplicavel",
    decimal RefundedAmount = 0,
    List<PurchaseRefundDto>? Refunds = null);

public record PurchaseRefundDto(Guid Id, decimal Amount, DateOnly Date, string? Notes, string CreatedBy, DateTime CreatedAt);

public record CancelPurchaseRequest(string? Reason);

public record RegisterPurchaseRefundRequest(decimal Amount, DateOnly? Date, string? Notes);

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

    /// <summary>
    /// Edita uma entrada existente: reverte o estoque somado pelos itens antigos, aplica os itens novos e
    /// recalcula a validade mais próxima dos produtos afetados (antigos e novos). Retorna null se não encontrada.
    /// </summary>
    Task<PurchaseEntryDto?> UpdateAsync(Guid id, SavePurchaseRequest request, CancellationToken ct);

    /// <summary>
    /// Cancela uma entrada (nunca apaga): estorna estoque e lotes por movimentações inversas, desfaz o efeito no custo médio
    /// e preserva o histórico. Compra paga fica com devolução pendente até RegisterRefundAsync. Recusa (InvalidOperationException)
    /// se parte do que entrou já foi vendida ou perdida. Retorna null se não encontrada.
    /// </summary>
    Task<PurchaseEntryDto?> CancelAsync(Guid id, string? reason, CancellationToken ct);

    /// <summary>Registra dinheiro devolvido pelo fornecedor por uma compra paga e cancelada (entra no caixa na data informada).</summary>
    Task<PurchaseEntryDto?> RegisterRefundAsync(Guid id, RegisterPurchaseRefundRequest request, CancellationToken ct);
}
