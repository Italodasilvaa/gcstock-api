namespace Girus.Application.Audit;

public record AuditLogEntryDto(
    string Id,
    DateTime Timestamp,
    string UserId,
    string UserName,
    string UserRole,
    string CompanyId,
    string Action,
    string EntityType,
    string EntityLabel,
    string? Details);

public interface IAuditQueryService
{
    Task<List<AuditLogEntryDto>> GetLogAsync(CancellationToken ct);
}

/// <summary>Serviço interno usado pelas demais camadas para registrar entradas de auditoria (não exposto via HTTP diretamente).</summary>
public interface IAuditRecorder
{
    Task RecordAsync(string action, string entityType, string entityLabel, string? details, CancellationToken ct);
}
