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
    bool CostEstimated,
    /// <summary>Quantidade deste item que já foi devolvida pelo cliente.</summary>
    decimal ReturnedQty = 0m);

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
    List<SaleItemDto> Items,
    /// <summary>Devoluções já registradas na venda (a venda original nunca é editada).</summary>
    List<SaleReturnDto>? Returns = null,
    decimal ReturnedAmount = 0m);

public record SaleReturnLineDto(string Name, decimal Qty, string Unit, decimal RefundAmount, bool Restocked);

public record SaleReturnDto(
    Guid Id,
    DateOnly Date,
    string CreatedBy,
    string Reason,
    decimal RefundAmount,
    decimal ReceivableAbated,
    decimal CashRefundAmount,
    List<SaleReturnLineDto> Lines);

/// <param name="Restock">true = item apto para revenda (volta ao estoque); false = não pode ser revendido (vira perda).</param>
public record ReturnSaleLine(Guid SaleItemId, decimal Qty, bool Restock);

/// <param name="RefundAmount">Valor devolvido ao cliente; vazio = calculado pelo servidor (preço pago, com desconto e taxa repassada proporcionais).</param>
public record ReturnSaleRequest(string? Reason, List<ReturnSaleLine>? Lines, decimal? RefundAmount = null);

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

    /// <summary>
    /// Devolução parcial (ou total dos itens) de uma venda: devolve ao estoque o que está apto para revenda, lança perda no que não está,
    /// abate os títulos a receber em aberto (o resto sai do caixa) e preserva a venda original. Retorna null se a venda não existir.
    /// </summary>
    Task<SaleHistoryDto?> ReturnItemsAsync(Guid saleId, ReturnSaleRequest request, CancellationToken ct);
}
