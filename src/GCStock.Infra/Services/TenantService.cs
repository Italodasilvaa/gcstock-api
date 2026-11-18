using GCStock.Application.Abstractions;
using GCStock.Application.Access;
using GCStock.Application.Audit;
using GCStock.Application.Auth;
using GCStock.Application.Tenants;
using GCStock.Infra.Database;
using GCStock.Infra.Mapping;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

public class TenantService(
    GCStockDbContext db,
    ICurrentUserContext currentUser,
    IJwtTokenService jwtTokenService,
    IAuditRecorder audit) : ITenantService
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
        var userDto = new AuthUserDto(currentUser.UserId, currentUser.UserName, currentUser.UserEmail, currentUser.Role, null, [.. Permissions.AllKeys]);
        return new AuthSessionDto(userDto, companyDto, token, DateTime.UtcNow);
    }

    public async Task<CompanySummaryDto?> UpdateCurrentCompanyAsync(UpdateCompanyRequest request, CancellationToken ct)
    {
        // Dados cadastrais são do dono: funcionário (mesmo gerente) não altera.
        if (!currentUser.IsOwner) return null;

        var corporateName = request.CorporateName?.Trim() ?? string.Empty;
        var document = request.Document?.Trim() ?? string.Empty;
        if (corporateName.Length == 0) throw new ArgumentException("Informe a razão social.");
        if (document.Length == 0) throw new ArgumentException("Informe o CNPJ ou CPF.");

        var state = request.State?.Trim().ToUpperInvariant() ?? string.Empty;
        if (state.Length != 2) throw new ArgumentException("Informe a UF com 2 letras.");

        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == currentUser.CompanyId, ct);
        if (company is null || company.OwnerId != currentUser.AdministratorId) return null;

        company.CorporateName = corporateName;
        company.TradeName = string.IsNullOrWhiteSpace(request.TradeName) ? corporateName : request.TradeName.Trim();
        company.Document = document;
        company.Phone = request.Phone?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(request.Segment)) company.Segment = request.Segment.Trim();
        if (!string.IsNullOrWhiteSpace(request.City)) company.City = request.City.Trim();
        company.State = state;

        // A matriz nasce com o nome, a cidade e o CNPJ da empresa: mantém em sincronia.
        var headquarters = await db.Branches.FirstOrDefaultAsync(b => b.CompanyId == company.Id && b.Type == "matriz", ct);
        if (headquarters is not null)
        {
            headquarters.Name = company.TradeName;
            headquarters.Location = $"{company.City} / {company.State}";
            headquarters.Cnpj = company.Document;
        }

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("editou", "Empresa", company.TradeName, "Dados cadastrais", ct);

        return await company.ToDtoAsync(db, ct);
    }

    public async Task<List<BranchDto>> GetBranchesAsync(CancellationToken ct)
    {
        return await db.Branches
            .Where(b => b.CompanyId == currentUser.CompanyId)
            .Select(b => new BranchDto(b.Id, b.Name, b.Location, b.Cnpj, b.OperatorsCount, b.Active, b.Type))
            .ToListAsync(ct);
    }
}
