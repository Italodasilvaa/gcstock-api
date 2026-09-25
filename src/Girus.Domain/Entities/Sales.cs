namespace Girus.Domain.Entities;

/// <summary>Type: "pf" | "pj".</summary>
public class Client
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Document { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Type { get; set; } = "pf";
    public int FidelityPoints { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal TotalSpent { get; set; }
    public string FavoriteCuts { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class SaleRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public Guid SaleId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public decimal Total { get; set; }
    public decimal NetAmount { get; set; }
    public decimal FeeAmount { get; set; }
    public string PaymentMode { get; set; } = string.Empty;
    public string? ClientName { get; set; }
    public string SoldBy { get; set; } = string.Empty;
}
