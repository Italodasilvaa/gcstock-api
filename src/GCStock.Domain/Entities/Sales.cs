namespace GCStock.Domain.Entities;

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

    /// <summary>Número do cupom (ex.: NFCe-123456); liga a venda ao título a receber e às movimentações de estoque.</summary>
    public string ReceiptNumber { get; set; } = string.Empty;
    public string? CardFlag { get; set; }
    public Guid? ClientId { get; set; }

    /// <summary>Desconto dado na venda: soma dos itens menos o total cobrado (0 quando não houve).</summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>"concluida" | "cancelada". Venda cancelada não entra nos relatórios financeiros.</summary>
    public string Status { get; set; } = "concluida";
    public DateTime? CanceledAt { get; set; }
    public string? CanceledBy { get; set; }
    public string? CancelReason { get; set; }

    public List<SaleItem> Items { get; set; } = [];
}

/// <summary>Item de uma venda do PDV (produto ou kit), com o preço unitário efetivamente cobrado.</summary>
public class SaleItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid SaleRecordId { get; set; }
    public SaleRecord? SaleRecord { get; set; }

    public Guid? ProductId { get; set; }
    public Guid? KitId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public string Unit { get; set; } = "kg";
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }

    /// <summary>false em vendas antigas (reconstruídas das baixas de estoque): o preço não foi guardado.</summary>
    public bool PriceKnown { get; set; } = true;

    /// <summary>Custo médio unitário do estoque no momento da venda (kit: soma do custo dos componentes de 1 kit).</summary>
    public decimal UnitCost { get; set; }

    /// <summary>Custo da mercadoria vendida neste item (quantidade × custo unitário): base do lucro.</summary>
    public decimal TotalCost { get; set; }

    /// <summary>true em vendas antigas: o custo foi estimado pelo custo médio atual do produto, não o do dia da venda.</summary>
    public bool CostEstimated { get; set; }

    /// <summary>De quais lotes do estoque esta venda tirou a quantidade (para devolver ao mesmo lote se for cancelada).</summary>
    public List<SaleItemLot> Lots { get; set; } = [];
}

/// <summary>Quantidade que um item de venda consumiu de um lote (pedido mais antigo primeiro, ou o lote escolhido). Sem FK para o lote: ele pode ser refeito por edição de compra.</summary>
public class SaleItemLot
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid SaleItemId { get; set; }
    public SaleItem? SaleItem { get; set; }

    public Guid StockBatchId { get; set; }
    public Guid ProductId { get; set; }
    public decimal Qty { get; set; }
}
