using Girus.Application.Abstractions;
using Girus.Application.Auth;
using Girus.Application.Tenants;
using Girus.Infra.Database;
using Girus.Infra.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Girus.Infra.Services;

public class TenantService(
    GirusDbContext db,
    ICurrentUserContext currentUser,
    IJwtTokenService jwtTokenService) : ITenantService
{
    public async Task<List<CompanySummaryDto>> GetMyCompaniesAsync(CancellationToken ct)
    {
        var currentCompany = await db.Companies.FirstOrDefaultAsync(c => c.Id == currentUser.CompanyId, ct);
        if (currentCompany is null) return [];

        var companies = await db.Companies
            .Where(c => c.OwnerId == currentCompany.OwnerId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        var result = new List<CompanySummaryDto>();
        foreach (var c in companies)
        {
            result.Add(await c.ToDtoAsync(db, ct));
        }
        return result;
    }

    public async Task<AuthSessionDto?> SwitchCompanyAsync(Guid companyId, CancellationToken ct)
    {
        // Só é permitida entre empresas do mesmo Administrator (mesmo dono/grupo), e só para o próprio dono
        // (funcionário não troca de empresa).
        if (!currentUser.IsOwner) return null;

        var target = await db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);
        var current = await db.Companies.FirstOrDefaultAsync(c => c.Id == currentUser.CompanyId, ct);
        if (target is null || current is null || target.OwnerId != current.OwnerId) return null;

        var admin = await db.Administrators.FirstAsync(a => a.Id == currentUser.AdministratorId, ct);

        var (token, _) = jwtTokenService.IssueToken(new JwtIssueRequest(
            currentUser.UserId, currentUser.UserName, currentUser.UserEmail, currentUser.Role, target.Id,
            AdministratorId: admin.Id, EmployeeId: null));

        var companyDto = await target.ToDtoAsync(db, ct);
        var userDto = new AuthUserDto(currentUser.UserId, currentUser.UserName, currentUser.UserEmail, currentUser.Role, null);
        return new AuthSessionDto(userDto, companyDto, token, DateTime.UtcNow);
    }

    public async Task<List<BranchDto>> GetBranchesAsync(CancellationToken ct)
    {
        return await db.Branches
            .Where(b => b.CompanyId == currentUser.CompanyId)
            .Select(b => new BranchDto(b.Id, b.Name, b.Location, b.Cnpj, b.OperatorsCount, b.Active, b.Type))
            .ToListAsync(ct);
    }
}
