namespace GCStock.Application.Clients;

public record ClientDto(
    Guid Id,
    string Name,
    string Document,
    string Phone,
    string? Email,
    string Type,
    int FidelityPoints,
    decimal CreditLimit,
    decimal TotalSpent,
    string FavoriteCuts,
    string? Notes,
    // Indicadores calculados das vendas (TotalSpent = total comprado já descontadas as devoluções).
    decimal TotalPaid = 0m,
    // Saldo em aberto: títulos a receber do cliente ainda não pagos (é o crédito já usado).
    decimal OpenBalance = 0m,
    int PurchaseCount = 0,
    DateOnly? FirstPurchase = null,
    DateOnly? LastPurchase = null,
    decimal AverageTicket = 0m,
    // Intervalo médio, em dias, entre compras; nulo com menos de 2 compras.
    decimal? FrequencyDays = null,
    // Limite menos o saldo em aberto (0 quando o limite não foi definido).
    decimal CreditAvailable = 0m,
    bool CreditExceeded = false);

public record SaveClientRequest(
    Guid? Id,
    string? Name,
    string? Document,
    string? Phone,
    string? Email,
    string? Type,
    int? FidelityPoints,
    decimal? CreditLimit,
    decimal? TotalSpent,
    string? FavoriteCuts,
    string? Notes);

public interface IClientService
{
    Task<List<ClientDto>> GetAllAsync(CancellationToken ct);
    Task<ClientDto> SaveAsync(SaveClientRequest request, CancellationToken ct);

    /// <summary>Histórico completo do cliente: todas as compras (qualquer pagamento), indicadores e o que mais compra. Nulo se não existir.</summary>
    Task<ClientHistoryDto?> GetHistoryAsync(Guid id, CancellationToken ct);
    /// <summary>Apaga o cliente; recusa (InvalidOperationException) se houver conta a receber em aberto dele.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
}

public record ClientPurchaseItemDto(string Name, decimal Qty, string Unit, decimal Subtotal, decimal ReturnedQty);

/// <summary>Compra do cliente, em qualquer forma de pagamento. Status: "concluida" | "cancelada".</summary>
public record ClientPurchaseDto(
    Guid SaleId,
    string ReceiptNumber,
    DateTime Timestamp,
    decimal Total,
    decimal ReturnedAmount,
    string PaymentMode,
    string Status,
    /// <summary>Quanto desta compra o cliente ainda deve (títulos a receber em aberto).</summary>
    decimal OpenAmount,
    List<ClientPurchaseItemDto> Items);

public record ClientTopItemDto(string Name, string Unit, decimal Qty);

public record ClientHistoryDto(
    ClientDto Client,
    List<ClientPurchaseDto> Purchases,
    List<ClientTopItemDto> TopProducts,
    List<ClientTopItemDto> TopKits);
