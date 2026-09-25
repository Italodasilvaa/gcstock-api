namespace Girus.Domain.Entities;

/// <summary>Status: "vencido" | "a_vencer" | "pago".</summary>
public class AccountsPayable
{
    public string Id { get; set; } = "pay_" + Guid.NewGuid().ToString("N");
    public string CompanyId { get; set; } = string.Empty;
    public Company? Company { get; set; }

    public string Supplier { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string NfeReference { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string DueLabel { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string Status { get; set; } = "a_vencer";
    public DateOnly? PaidDate { get; set; }
    public bool ReceiptAttached { get; set; }

    /// <summary>"compras" | "equipamento" | "investimento" | "outros" — usada na Gestão da Empresa e no fechamento mensal.</summary>
    public string Category { get; set; } = "outros";

    /// <summary>Compra (Módulo Compras) que originou o título; ao dar baixa, a compra também passa a "pago".</summary>
    public string? PurchaseEntryId { get; set; }
}

/// <summary>Status: "vencido" | "a_vencer" | "recebido". Type: "b2b" | "adquirente" | "evento".</summary>
public class AccountsReceivable
{
    public string Id { get; set; } = "rec_" + Guid.NewGuid().ToString("N");
    public string CompanyId { get; set; } = string.Empty;
    public Company? Company { get; set; }

    public string ClientOrOrigin { get; set; } = string.Empty;
    public string SubCategory { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public string DueLabel { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal FeeRetention { get; set; }
    public decimal NetAmount { get; set; }
    public string Status { get; set; } = "a_vencer";
    public string Type { get; set; } = "b2b";
    public string PaymentModeDesc { get; set; } = string.Empty;
    public DateOnly? ReceivedDate { get; set; }

    public string? ClientId { get; set; }
}

public class PaymentFeeSetting
{
    public string Id { get; set; } = "fee_" + Guid.NewGuid().ToString("N");
    public string CompanyId { get; set; } = string.Empty;
    public Company? Company { get; set; }

    public string Method { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public decimal FeePercent { get; set; }
    public decimal? FixedFee { get; set; }
    public string SettlementTerm { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public string Icon { get; set; } = string.Empty;
    /// <summary>Posição na lista de Configurações e nos cards de pagamento do PDV.</summary>
    public int SortOrder { get; set; }
}

/// <summary>Status: "a_liquidar" | "liquidado" | "antecipado".</summary>
public class CardSettlement
{
    public string Id { get; set; } = "card_" + Guid.NewGuid().ToString("N");
    public string CompanyId { get; set; } = string.Empty;
    public Company? Company { get; set; }

    public string Acquirer { get; set; } = string.Empty;
    public string CardBrand { get; set; } = string.Empty;
    public DateOnly SaleDate { get; set; }
    public string SettlementDate { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal MdrFeeAmount { get; set; }
    public decimal NetAmount { get; set; }
    public string Status { get; set; } = "a_liquidar";
    public decimal? AnticipatedFee { get; set; }
}

/// <summary>Configuração global de alertas sanitários, uma por empresa.</summary>
public class AlertSetting
{
    public string CompanyId { get; set; } = string.Empty;
    public Company? Company { get; set; }

    public int AlertLeadDays { get; set; } = 3;
    public string MorningSummaryTime { get; set; } = "07:30";
    public bool NotifyTopbar { get; set; } = true;
    public bool NotifyEmail { get; set; } = true;
    public bool NotifyWhatsapp { get; set; } = true;

    /// <summary>Lista de destinatários serializada com '|' como separador (ver GirusDbContext).</summary>
    public List<string> Recipients { get; set; } = [];
}
