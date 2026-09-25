namespace Girus.Domain.Entities;

/// <summary>PaymentStatus: "pago" | "pendente". PaymentMode: "pix" | "dinheiro" | "cartao" | "a_prazo".</summary>
public class PurchaseEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public string OrderCode { get; set; } = string.Empty;
    public string NfeNumber { get; set; } = string.Empty;
    public string NfeSeries { get; set; } = string.Empty;
    public DateOnly EntryDate { get; set; }
    public string EntryTime { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string SupplierCnpj { get; set; } = string.Empty;
    public string SupplierCity { get; set; } = string.Empty;
    public string SupplierSif { get; set; } = string.Empty;
    public int ItemsCount { get; set; }
    public decimal TotalWeightKg { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal ShippingCost { get; set; }
    public string PaymentCondition { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = "pendente";
    public string PaymentMode { get; set; } = "a_prazo";

    public List<PurchaseItem> Items { get; set; } = [];
}

public class PurchaseItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PurchaseEntryId { get; set; }
    public PurchaseEntry? PurchaseEntry { get; set; }

    /// <summary>Produto do catálogo (Girus.Domain.Entities.Product) ao qual esta entrada deve somar estoque. Nulo = item avulso, sem baixa automática.</summary>
    public Guid? ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "kg";
    public decimal UnitCost { get; set; }
    public decimal Subtotal { get; set; }
    /// <summary>Nulo quando o item não tem data de validade informada (ex.: produto não perecível).</summary>
    public DateOnly? ExpiryDate { get; set; }
    public string LotNumber { get; set; } = string.Empty;
    public bool IsPerishable { get; set; }
}
