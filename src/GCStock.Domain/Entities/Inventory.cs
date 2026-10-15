namespace GCStock.Domain.Entities;

/// <summary>Status: "urgente" | "regular" | "novo" | "vencido".</summary>
public class StockBatch
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string LotNumber { get; set; } = string.Empty;
    /// <summary>Quantidade disponível do lote, na unidade do produto (o nome "Kg" é histórico: pode ser kg, pct, un...).</summary>
    public decimal AvailableKg { get; set; }
    /// <summary>Unidade do produto (cadastro): "kg" | "un" | "pct" | "cx".</summary>
    public string Unit { get; set; } = "kg";
    public DateOnly EntryDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public int DaysRemaining { get; set; }
    public string FiscalDoc { get; set; } = string.Empty;
    public string Supplier { get; set; } = string.Empty;
    public string Status { get; set; } = "novo";
    /// <summary>Compra de origem deste lote — permite reverter/refazer o lote quando a compra é editada.</summary>
    public Guid? PurchaseEntryId { get; set; }
}

/// <summary>Type: "entrada" | "baixa_pdv" | "perda_quebra".</summary>
public class StockMovement
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = "entrada";
    public string Title { get; set; } = string.Empty;
    public string DocInfo { get; set; } = string.Empty;
    /// <summary>Quantidade movimentada na unidade em <see cref="Unit"/> (o nome "Kg" é histórico). Negativo = saída.</summary>
    public decimal AmountKg { get; set; }
    /// <summary>Unidade da quantidade: "kg" | "un" | "pct" | "cx".</summary>
    public string Unit { get; set; } = "kg";
    public string Description { get; set; } = string.Empty;
    public string BadgeLabel { get; set; } = string.Empty;
    public string AuditLink { get; set; } = string.Empty;
    /// <summary>Compra de origem deste movimento — permite removê-lo quando a compra é editada.</summary>
    public Guid? PurchaseEntryId { get; set; }
}

/// <summary>
/// Perda de estoque (vencimento, quebra, furto...) com o valor do prejuízo: quantidade × custo unitário informado.
/// Status: "ativa" | "desfeita" (desfeita devolveu a quantidade ao estoque e não conta no resultado).
/// </summary>
public class StockLoss
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    /// <summary>Dia da perda (data local); é o que define o mês do prejuízo no resultado.</summary>
    public DateOnly Date { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public string Unit { get; set; } = "kg";

    /// <summary>Custo unitário da perda (o do lote perdido, ou o custo médio quando não informado).</summary>
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }

    /// <summary>"vencimento" | "quebra" | "furto" | "outros".</summary>
    public string Reason { get; set; } = "outros";
    public string? Notes { get; set; }

    public Guid? StockBatchId { get; set; }
    public string LotNumber { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;

    public string Status { get; set; } = "ativa";
    public DateTime? UndoneAt { get; set; }
    public string? UndoneBy { get; set; }

    /// <summary>De quais lotes saiu a quantidade (para devolver ao mesmo lote se a perda for desfeita).</summary>
    public List<StockLossLot> Lots { get; set; } = [];
}

/// <summary>Quantidade de uma perda que saiu de um lote. Sem FK para o lote: ele pode ser refeito por edição de compra.</summary>
public class StockLossLot
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid StockLossId { get; set; }
    public StockLoss? StockLoss { get; set; }

    public Guid StockBatchId { get; set; }
    public decimal Qty { get; set; }
}
