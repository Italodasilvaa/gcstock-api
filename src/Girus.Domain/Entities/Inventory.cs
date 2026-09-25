namespace Girus.Domain.Entities;

/// <summary>Status: "urgente" | "regular" | "novo" | "vencido".</summary>
public class StockBatch
{
    public string Id { get; set; } = "batch_" + Guid.NewGuid().ToString("N");
    public string CompanyId { get; set; } = string.Empty;
    public Company? Company { get; set; }

    public string ProductId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string LotNumber { get; set; } = string.Empty;
    public decimal AvailableKg { get; set; }
    public DateOnly EntryDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public int DaysRemaining { get; set; }
    public string FiscalDoc { get; set; } = string.Empty;
    public string Supplier { get; set; } = string.Empty;
    public string Status { get; set; } = "novo";
    /// <summary>Compra de origem deste lote — permite reverter/refazer o lote quando a compra é editada.</summary>
    public string? PurchaseEntryId { get; set; }
}

/// <summary>Type: "entrada" | "baixa_pdv" | "perda_quebra".</summary>
public class StockMovement
{
    public string Id { get; set; } = "mov_" + Guid.NewGuid().ToString("N");
    public string CompanyId { get; set; } = string.Empty;
    public Company? Company { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = "entrada";
    public string Title { get; set; } = string.Empty;
    public string DocInfo { get; set; } = string.Empty;
    public decimal AmountKg { get; set; }
    public string Description { get; set; } = string.Empty;
    public string BadgeLabel { get; set; } = string.Empty;
    public string AuditLink { get; set; } = string.Empty;
    /// <summary>Compra de origem deste movimento — permite removê-lo quando a compra é editada.</summary>
    public string? PurchaseEntryId { get; set; }
}
