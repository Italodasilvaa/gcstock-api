namespace GCStock.Application.Sales;

/// <summary>Item de uma venda do PDV. <paramref name="PriceKnown"/> é false em vendas antigas, cujos preços não foram guardados.</summary>
public record SaleItemDto(
    Guid Id,
    string Name,
    decimal Qty,
    string Unit,
    decimal UnitPrice,
    decimal Subtotal,
    bool PriceKnown,
    decimal UnitCost,
    decimal TotalCost,
    bool CostEstimated);

/// <summary>Venda do PDV para o histórico. Status: "concluida" | "cancelada".</summary>
public record SaleHistoryDto(
    Guid Id,
    Guid SaleId,
    string ReceiptNumber,
    DateTime Timestamp,
    decimal Total,
    decimal NetAmount,
    decimal FeeAmount,
    decimal DiscountAmount,
    string PaymentMode,
    string? CardFlag,
    string? ClientName,
    string SoldBy,
    string Status,
    DateTime? CanceledAt,
    string? CanceledBy,
    string? CancelReason,
    /// <summary>Custo das mercadorias vendidas (soma do custo dos itens).</summary>
    decimal CostTotal,
    /// <summary>Lucro bruto da venda: valor líquido recebido − custo das mercadorias.</summary>
    decimal Profit,
    List<SaleItemDto> Items);

public record CancelSaleRequest(string? Reason);

/// <param name="Notice">Aviso quando algo não pôde ser desfeito (ex.: item antigo sem vínculo com o cadastro).</param>
public record CancelSaleResult(SaleHistoryDto Sale, string? Notice);

public interface ISaleService
{
    Task<List<SaleHistoryDto>> GetAllAsync(CancellationToken ct);

    /// <summary>
    /// Cancela (estorna) uma venda: devolve o estoque dos itens, remove o título a receber gerado por ela e marca a
    /// venda como cancelada (ela deixa de entrar nos relatórios financeiros). Retorna null se a venda não existir.
    /// Lança <see cref="InvalidOperationException"/> se já estiver cancelada ou se o recebível já foi liquidado.
    /// </summary>
    Task<CancelSaleResult?> CancelAsync(Guid saleId, string? reason, CancellationToken ct);
}
