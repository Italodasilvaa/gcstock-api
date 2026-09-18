namespace Girus.Domain.Entities;

/// <summary>PaymentStatus: "pago" | "pendente". PaymentMode: "pix" | "dinheiro" | "cartao" | "a_prazo".</summary>
public class PurchaseEntry
{
    public string Id { get; set; } = "pur_" + Guid.NewGuid().ToString("N");
    public string CompanyId { get; set; } = string.Empty;
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
    public string Id { get; set; } = "puritem_" + Guid.NewGuid().ToString("N");
    public string PurchaseEntryId { get; set; } = string.Empty;
    public PurchaseEntry? PurchaseEntry { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "kg";
    public decimal UnitCost { get; set; }
    public decimal Subtotal { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public string LotNumber { get; set; } = string.Empty;
    public bool IsPerishable { get; set; }
}
