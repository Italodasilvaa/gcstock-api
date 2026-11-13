namespace GCStock.Application.Pdv;

public record PosSaleItem(
    string Name,
    decimal Qty,
    string Unit,
    decimal Price,
    // Informado quando o item vendido é um produto avulso do catálogo — a venda baixa Qty do estoque dele.
    Guid? ProductId = null,
    // Informado quando o item vendido é um Kit de Churrasco — a venda baixa (Qty * quantidade do componente) de cada produto que compõe o kit.
    Guid? KitId = null,
    // Lote de onde o produto avulso sai (opcional): o custo do item passa a ser o do lote, para o lucro real. Vazio = sai do pedido mais antigo, a custo médio.
    Guid? StockBatchId = null);

public record FinalizeSaleRequest(
    decimal Total,
    string PaymentMode,
    string? CardFlag,
    Guid? ClientId,
    string? ClientName,
    DateOnly? DueDate,
    decimal FeeAmount,
    decimal NetAmount,
    List<PosSaleItem> Items,
    // Desconto dado na venda (R$), validado entre 0 e o total dos itens.
    decimal Discount = 0m,
    // Forma de pagamento escolhida (id da taxa em Configurações): é de lá que o servidor tira a taxa e o tipo de pagamento.
    Guid? PaymentOptionId = null,
    // true = o cliente paga a taxa (total = base + taxa); false = a taxa sai do valor recebido.
    bool PassFeeToCustomer = true,
    // Parcelas (cartão parcelado ou venda a prazo): 1 a 12.
    int Installments = 1);

/// <summary>
/// Valores definitivos calculados pelo servidor. Total/Net/Fee/Discount são os que ficaram gravados; a tela só os exibe.
/// </summary>
public record FinalizeSaleResult(
    Guid SaleId,
    string ReceiptNumber,
    decimal Total = 0m,
    decimal NetAmount = 0m,
    decimal FeeAmount = 0m,
    decimal DiscountAmount = 0m,
    int Installments = 1);

public interface IPosService
{
    /// <summary>
    /// Finaliza uma venda de balcão: cria a movimentação de baixa de estoque,
    /// gera título a receber quando o pagamento é a crédito/parcelado/a prazo,
    /// e registra o histórico de vendas.
    /// </summary>
    Task<FinalizeSaleResult> FinalizeSaleAsync(FinalizeSaleRequest request, CancellationToken ct);
}
