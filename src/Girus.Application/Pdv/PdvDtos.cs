namespace Girus.Application.Pdv;

public record PosSaleItem(string Name, decimal Qty, string Unit, decimal Price);

public record FinalizeSaleRequest(
    decimal Total,
    string PaymentMode,
    string? CardFlag,
    string? ClientId,
    string? ClientName,
    DateOnly? DueDate,
    decimal FeeAmount,
    decimal NetAmount,
    List<PosSaleItem> Items);

public record FinalizeSaleResult(string SaleId, string ReceiptNumber);

public interface IPosService
{
    /// <summary>
    /// Finaliza uma venda de balcão: cria a movimentação de baixa de estoque,
    /// gera título a receber quando o pagamento é a crédito/parcelado/a prazo,
    /// e registra o histórico de vendas.
    /// </summary>
    Task<FinalizeSaleResult> FinalizeSaleAsync(FinalizeSaleRequest request, CancellationToken ct);
}
