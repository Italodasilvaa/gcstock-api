using Girus.Application.Abstractions;
using Girus.Application.Audit;
using Girus.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace Girus.Infra.Services;

public class AuditQueryService(GirusDbContext db, ICurrentUserContext currentUser) : IAuditQueryService
{
    public async Task<List<AuditLogEntryDto>> GetLogAsync(CancellationToken ct)
    {
        return await db.AuditLogEntries
            .Where(a => a.CompanyId == currentUser.CompanyId)
            .OrderByDescending(a => a.Timestamp)
            .Select(a => new AuditLogEntryDto(
                a.Id, a.Timestamp, a.UserId, a.UserName, a.UserRole, a.CompanyId, a.Action, a.EntityType, a.EntityLabel, a.Details))
            .ToListAsync(ct);
    }
}
