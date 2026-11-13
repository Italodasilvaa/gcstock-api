namespace GCStock.Domain.Entities;

/// <summary>
/// Cada vez que o custo (ou a margem) de um kit muda: custo e margem antes e depois, o motivo e o produto que causou.
/// Source: "recalculo_automatico" (custo de um componente mudou) | "edicao_kit". Nunca altera vendas já feitas.
/// </summary>
public class KitCostHistory
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CompanyId { get; set; }
    public Guid KitId { get; set; }
    public string KitName { get; set; } = string.Empty;

    public decimal PreviousCost { get; set; }
    public decimal NewCost { get; set; }
    public decimal PreviousMarginPercent { get; set; }
    public decimal NewMarginPercent { get; set; }
    /// <summary>Preço de venda no momento (ele não muda no recálculo; fica registrado para ler a margem).</summary>
    public decimal SuggestedPrice { get; set; }

    public string Source { get; set; } = "recalculo_automatico";
    /// <summary>Produto cujo novo custo disparou o recálculo (nulo na edição do próprio kit).</summary>
    public string? TriggerProduct { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
}
