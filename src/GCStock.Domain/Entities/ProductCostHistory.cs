namespace GCStock.Domain.Entities;

/// <summary>
/// Cada mudança do custo médio de um produto: de quanto para quanto, por quê, em qual compra e quem fez.
/// Nunca é editada nem apagada, e não altera o custo gravado nas vendas já feitas (SaleItem.UnitCost).
/// Source: "compra" | "edicao_compra" | "cancelamento_compra" | "ajuste_manual".
/// </summary>
public class ProductCostHistory
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CompanyId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;

    public decimal PreviousCost { get; set; }
    public decimal NewCost { get; set; }

    public string Source { get; set; } = "compra";
    public Guid? PurchaseEntryId { get; set; }
    /// <summary>Código do pedido (PED-...) no momento da mudança, para exibir sem depender da compra continuar existindo.</summary>
    public string? PurchaseCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
}
