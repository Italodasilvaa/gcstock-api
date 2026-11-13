namespace GCStock.Domain.Entities;

/// <summary>
/// Devolução (parcial ou total dos itens) de uma venda já finalizada. A venda original nunca é editada: a devolução é um
/// registro à parte, com o valor devolvido ao cliente, o que voltou ao estoque e o que virou perda.
/// </summary>
public class SaleReturn
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CompanyId { get; set; }
    public Guid SaleRecordId { get; set; }
    public SaleRecord? SaleRecord { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;

    /// <summary>Dia (horário de Brasília) da devolução: é o que define o mês no caixa e na DRE.</summary>
    public DateOnly Date { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;

    /// <summary>Valor devolvido ao cliente (R$).</summary>
    public decimal RefundAmount { get; set; }

    /// <summary>Parte do valor que abateu títulos a receber ainda em aberto (não sai do caixa porque nunca entrou).</summary>
    public decimal ReceivableAbated { get; set; }

    /// <summary>Parte que efetivamente sai do caixa (o dinheiro já tinha sido recebido): valor devolvido menos o abatido.</summary>
    public decimal CashRefundAmount { get; set; }

    /// <summary>Receita líquida (depois de desconto e taxa) que deixa de existir na DRE por causa da devolução.</summary>
    public decimal NetRevenueReturned { get; set; }

    /// <summary>Custo das mercadorias devolvidas: sai do custo das mercadorias vendidas (as perdidas entram na linha de perdas).</summary>
    public decimal CostReturned { get; set; }

    public List<SaleReturnItem> Items { get; set; } = [];
}

public class SaleReturnItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid SaleReturnId { get; set; }
    public SaleReturn? SaleReturn { get; set; }

    public Guid SaleItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public string Unit { get; set; } = "kg";

    /// <summary>Valor devolvido por este item (R$).</summary>
    public decimal RefundAmount { get; set; }

    /// <summary>true = apto para revenda (voltou ao estoque); false = virou perda (não voltou).</summary>
    public bool Restocked { get; set; }
}
