using Girus.Application.Abstractions;
using Girus.Application.Audit;
using Girus.Domain.Entities;
using Girus.Infra.Database;

namespace Girus.Infra.Services;

public class AuditRecorder(GirusDbContext db, ICurrentUserContext currentUser) : IAuditRecorder
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
