namespace GCStock.Application.Pdv;

public record PosSaleItem(
    string Name,
    decimal Qty,
    string Unit,
    decimal Price,
    // Informado quando o item vendido é um produto avulso do catálogo — a venda baixa Qty do estoque dele.
    Guid? ProductId = null,
    // Informado quando o item vendido é um Kit de Churrasco — a venda baixa (Qty * quantidade do componente) de cada produto que compõe o kit.
    Guid? KitId = null);

public record FinalizeSaleRequest(
    decimal Total,
    string PaymentMode,
    string? CardFlag,
    Guid? ClientId,
    string? ClientName,
    DateOnly? DueDate,
    decimal FeeAmount,
    decimal NetAmount,
    List<PosSaleItem> Items);

public record FinalizeSaleResult(Guid SaleId, string ReceiptNumber);

public interface IPosService
{
    /// <summary>
    /// Finaliza uma venda de balcão: cria a movimentação de baixa de estoque,
    /// gera título a receber quando o pagamento é a crédito/parcelado/a prazo,
    /// e registra o histórico de vendas.
    /// </summary>
    Task<FinalizeSaleResult> FinalizeSaleAsync(FinalizeSaleRequest request, CancellationToken ct);
}
