namespace Girus.Domain.Entities;

/// <summary>UserRole: "admin" | "gerente" | "operador".
/// Action: "criou" | "editou" | "excluiu" | "vendeu" | "baixou" | "recebeu" | "antecipou" | "liquidou".</summary>
public class AuditLogEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserRole { get; set; } = "operador";
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityLabel { get; set; } = string.Empty;
    public string? Details { get; set; }
}
