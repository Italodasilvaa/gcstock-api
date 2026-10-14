using GCStock.Application.Abstractions;
using GCStock.Application.Audit;
using GCStock.Domain.Entities;
using GCStock.Infra.Database;

namespace GCStock.Infra.Services;

public class AuditRecorder(GCStockDbContext db, ICurrentUserContext currentUser) : IAuditRecorder
{
    public async Task RecordAsync(string action, string entityType, string entityLabel, string? details, CancellationToken ct)
    {
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            CompanyId = currentUser.CompanyId,
            UserId = currentUser.UserId,
            UserName = currentUser.UserName,
            UserRole = currentUser.Role,
            Action = action,
            EntityType = entityType,
            EntityLabel = entityLabel,
            Details = details,
        });
        await db.SaveChangesAsync(ct);
    }
}
