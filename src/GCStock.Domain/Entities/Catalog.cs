namespace GCStock.Domain.Entities;

/// <summary>Unit: "kg" | "un" | "pct" | "cx". ExpiryStatus: "vencido" | "critico" | "normal" | "longo".</summary>
public class Product
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CompanyId { get; set; }
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

/// <summary>Categoria de produto da empresa. Product.Category guarda o Name (texto), então renomear aqui propaga para os produtos.</summary>
public class ProductCategory
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "category";
    public int SortOrder { get; set; }
}

/// <summary>StockStatus: "amplo" | "disponivel" | "alerta".</summary>
public class Kit
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CompanyId { get; set; }
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
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid KitId { get; set; }
    public Kit? Kit { get; set; }

    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string LotNumber { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "kg";
    public decimal UnitCost { get; set; }
    public decimal Subtotal { get; set; }
    public decimal AvailableStock { get; set; }
}
