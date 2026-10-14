using GCStock.Application.Auth;
using GCStock.Domain.Entities;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Mapping;

public static class CompanyMapper
{
    /// <summary>Carrega o nome do dono e a contagem de filiais separadamente — usar quando a entidade
    /// não veio com a navegação <c>Owner</c> incluída (Include).</summary>
    public static async Task<CompanySummaryDto> ToDtoAsync(this Company c, GCStockDbContext db, CancellationToken ct)
    {
        var ownerName = c.Owner?.Name
            ?? await db.Administrators.Where(a => a.Id == c.OwnerId).Select(a => a.Name).FirstOrDefaultAsync(ct)
            ?? string.Empty;
        var branchesCount = await db.Branches.CountAsync(b => b.CompanyId == c.Id, ct);
        return c.ToDto(ownerName, branchesCount);
    }

    public static CompanySummaryDto ToDto(this Company c, string ownerName, int branchesCount) => new(
        c.Id,
        c.CorporateName,
        c.TradeName,
        c.Document,
        c.OwnerId,
        ownerName,
        c.Phone,
        c.Segment,
        c.City,
        c.State,
        DateOnly.FromDateTime(c.CreatedAt),
        c.Active,
        branchesCount,
        c.Plan);
}
