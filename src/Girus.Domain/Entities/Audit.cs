namespace Girus.Domain.Entities;

/// <summary>UserRole: "admin" | "gerente" | "operador".
/// Action: "criou" | "editou" | "excluiu" | "vendeu" | "baixou" | "recebeu" | "antecipou" | "liquidou".</summary>
public class AuditLogEntry
{
    public string Id { get; set; } = "audit_" + Guid.NewGuid().ToString("N");
    public string CompanyId { get; set; } = string.Empty;
    public Company? Company { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserRole { get; set; } = "operador";
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityLabel { get; set; } = string.Empty;
    public string? Details { get; set; }
}
