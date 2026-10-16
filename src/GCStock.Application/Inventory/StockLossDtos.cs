namespace GCStock.Application.Inventory;

/// <summary>Motivos aceitos para uma perda de estoque.</summary>
public static class LossReasons
{
    public static readonly IReadOnlyList<string> All = ["vencimento", "quebra", "furto", "outros"];

    public static bool IsValid(string? reason) => reason is not null && All.Contains(reason);

    public static string Label(string reason) => reason switch
    {
        "vencimento" => "Vencimento",
        "quebra" => "Quebra / avaria",
        "furto" => "Furto / extravio",
        _ => "Outros",
    };
}

/// <summary>Perda de estoque lançada. Status: "ativa" | "desfeita" (desfeita não conta no resultado).</summary>
public record StockLossDto(
    Guid Id,
    DateOnly Date,
    DateTime CreatedAt,
    Guid ProductId,
    string ProductName,
    decimal Qty,
    string Unit,
    decimal UnitCost,
    decimal TotalCost,
    string Reason,
    string? Notes,
    string LotNumber,
    string CreatedBy,
    string Status,
    DateTime? UndoneAt,
    string? UndoneBy);

/// <param name="UnitCost">Custo unitário da perda (o do lote perdido); vazio = custo médio atual do produto.</param>
/// <param name="StockBatchId">Lote que foi perdido (opcional); vazio = sai primeiro o pedido mais antigo.</param>
/// <param name="Date">Dia da perda (padrão: hoje, no horário de Brasília).</param>
public record CreateStockLossRequest(
    Guid ProductId,
    decimal Qty,
    decimal? UnitCost,
    string Reason,
    string? Notes,
    DateOnly? Date,
    Guid? StockBatchId);

public interface IStockLossService
{
    Task<List<StockLossDto>> GetAllAsync(CancellationToken ct);

    /// <summary>
    /// Registra uma perda: abate a quantidade do estoque do produto (e do lote), grava a movimentação de perda e o valor
    /// do prejuízo (quantidade × custo unitário informado). Lança <see cref="ArgumentException"/> para dados inválidos e
    /// <see cref="InvalidOperationException"/> quando a quantidade passa do estoque (ou do lote escolhido).
    /// </summary>
    Task<StockLossDto> CreateAsync(CreateStockLossRequest request, CancellationToken ct);

    /// <summary>Desfaz uma perda lançada por engano: devolve a quantidade ao estoque (ao mesmo lote) e tira o prejuízo do resultado.</summary>
    Task<StockLossDto?> UndoAsync(Guid id, CancellationToken ct);
}
