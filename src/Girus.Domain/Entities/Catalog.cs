namespace Girus.Domain.Entities;

/// <summary>Unit: "kg" | "un" | "pct" | "cx". ExpiryStatus: "vencido" | "critico" | "normal" | "longo".</summary>
public class Product
{
    public string Id { get; set; } = "prod_" + Guid.NewGuid().ToString("N");
    public string CompanyId { get; set; } = string.Empty;
    public Company? Company { get; set; }

    public string Sku { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Ean { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = "kg";
    public decimal Stock { get; set; }
    public decimal MinStock { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public bool IsPerishable { get; set; } = true;
    public int AlertDays { get; set; } = 3;
    public string StorageTemp { get; set; } = string.Empty;
    public DateOnly NearestExpiry { get; set; }
    public int ExpiryDaysRemaining { get; set; }
    public string ExpiryStatus { get; set; } = "normal";
    public string LotNumber { get; set; } = string.Empty;
    public string Supplier { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string? SubLocation { get; set; }
    public bool IsPromo { get; set; }
}

/// <summary>StockStatus: "amplo" | "disponivel" | "alerta".</summary>
public class Kit
{
    public string Id { get; set; } = "kit_" + Guid.NewGuid().ToString("N");
    public string CompanyId { get; set; } = string.Empty;
    public Company? Company { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string ServeLabel { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal SuggestedPrice { get; set; }
    public decimal CostPrice { get; set; }
    public decimal MarginPercent { get; set; }
    public decimal TotalWeightKg { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string ComponentsSummary { get; set; } = string.Empty;
    public decimal StockAvailable { get; set; }
    public string StockStatus { get; set; } = "disponivel";
    public string BarcodeEan { get; set; } = string.Empty;

    public List<KitComponent> Components { get; set; } = [];
}

public class KitComponent
{
    public string Id { get; set; } = "kitcomp_" + Guid.NewGuid().ToString("N");
    public string KitId { get; set; } = string.Empty;
    public Kit? Kit { get; set; }

    public string ProductId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string LotNumber { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "kg";
    public decimal UnitCost { get; set; }
    public decimal Subtotal { get; set; }
    public decimal AvailableStock { get; set; }
}
