using Girus.Application.Abstractions;
using Girus.Application.Auth;
using Girus.Domain.Entities;
using Girus.Infra.Database;
using Girus.Infra.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Girus.Infra.Services;

public class AuthService(
    GirusDbContext db,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    ICurrentUserContext currentUser) : IAuthService
{
    public async Task<AuthSessionDto> RegisterCompanyAsync(RegisterCompanyRequest request, CancellationToken ct)
    {
        var cleanEmail = request.Email.Trim().ToLowerInvariant();

        // Se já existe um Administrator com esse e-mail, a nova empresa é adicionada ao grupo dele
        // (mesmo dono, N empresas) em vez de criar um dono duplicado.
        var admin = await db.Administrators.FirstOrDefaultAsync(a => a.Email.ToLower() == cleanEmail, ct);
        if (admin is null)
        {
            admin = new Administrator
            {
                Name = request.OwnerName,
                Email = request.Email,
                PasswordHash = passwordHasher.Hash(request.Password ?? "123456"),
            };
            db.Administrators.Add(admin);
        }

        var company = new Company
        {
            CorporateName = request.CorporateName,
            TradeName = string.IsNullOrWhiteSpace(request.TradeName) ? request.CorporateName : request.TradeName,
            Document = request.Document,
            OwnerId = admin.Id,
            Phone = request.Phone,
            Segment = string.IsNullOrWhiteSpace(request.Segment) ? "Açougue Boutique" : request.Segment,
            City = string.IsNullOrWhiteSpace(request.City) ? "São Paulo" : request.City,
            State = string.IsNullOrWhiteSpace(request.State) ? "SP" : request.State,
        };
        db.Companies.Add(company);

        db.Branches.Add(new Branch
        {
            CompanyId = company.Id,
            Name = company.TradeName,
            Location = $"{company.City} / {company.State}",
            Cnpj = company.Document,
            OperatorsCount = 1,
            Type = "matriz",
        });

        db.AlertSettings.Add(new AlertSetting { CompanyId = company.Id });

        if (request.Employees is { Count: > 0 })
        {
            foreach (var emp in request.Employees)
            {
                if (string.IsNullOrWhiteSpace(emp.Name) || string.IsNullOrWhiteSpace(emp.Email)) continue;

                db.Employees.Add(new Employee
                {
                    CompanyId = company.Id,
                    Name = emp.Name,
                    Email = emp.Email,
                    PasswordHash = passwordHasher.Hash(string.IsNullOrWhiteSpace(emp.Password) ? "123456" : emp.Password),
                    Role = string.IsNullOrWhiteSpace(emp.Role) ? "operador" : emp.Role,
                    Active = true,
                });
            }
        }

        await db.SaveChangesAsync(ct);

        db.AuditLogEntries.Add(new AuditLogEntry
        {
            CompanyId = company.Id,
            UserId = "usr-" + admin.Id,
            UserName = admin.Name,
            UserRole = "admin",
            Action = "criou",
            EntityType = "Empresa",
            EntityLabel = company.TradeName,
        });
        await db.SaveChangesAsync(ct);

        return await BuildSessionAsync(admin, company, employeeId: null, role: "admin", userName: admin.Name, ct: ct);
    }

    public async Task<AuthSessionDto?> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var cleanEmail = request.Email.Trim().ToLowerInvariant();

        // 1. Dono/administrador (pode ter várias empresas — entra na mais recente).
        var admin = await db.Administrators.FirstOrDefaultAsync(a => a.Email.ToLower() == cleanEmail, ct);
        if (admin is not null && passwordHasher.Verify(request.Password ?? string.Empty, admin.PasswordHash))
        {
            var company = await db.Companies
                .Where(c => c.OwnerId == admin.Id)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (company is not null)
            {
                return await BuildSessionAsync(admin, company, employeeId: null, role: "admin", userName: admin.Name, ct: ct);
            }
        }

        // 2. Funcionário cadastrado pelo administrador da empresa (vinculado a UMA empresa).
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Email.ToLower() == cleanEmail && e.Active, ct);
        if (employee is not null && passwordHasher.Verify(request.Password ?? string.Empty, employee.PasswordHash))
        {
            var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == employee.CompanyId, ct);
            if (company is not null)
            {
                var employeeAdmin = await db.Administrators.FirstAsync(a => a.Id == company.OwnerId, ct);
                return await BuildSessionAsync(employeeAdmin, company, employeeId: employee.Id, role: employee.Role, userName: employee.Name,
                    userId: employee.Id, userEmail: employee.Email, ct: ct);
            }
        }

        return null;
    }

    public async Task<AuthSessionDto?> GetCurrentSessionAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return null;

        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == currentUser.CompanyId, ct);
        if (company is null) return null;

        var companyDto = await company.ToDtoAsync(db, ct);
        var user = new AuthUserDto(currentUser.UserId, currentUser.UserName, currentUser.UserEmail, currentUser.Role, currentUser.EmployeeId);
        return new AuthSessionDto(user, companyDto, string.Empty, DateTime.UtcNow);
    }

    public async Task<List<CompanySummaryDto>> ListCompaniesForLoginAsync(CancellationToken ct)
    {
        var companies = await db.Companies.Include(c => c.Owner).OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        var result = new List<CompanySummaryDto>();
        foreach (var c in companies)
        {
            result.Add(await c.ToDtoAsync(db, ct));
        }
        return result;
    }

    public async Task<string?> GetCompanyOwnerEmailAsync(string companyId, CancellationToken ct)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null) return null;
        return await db.Administrators.Where(a => a.Id == company.OwnerId).Select(a => a.Email).FirstOrDefaultAsync(ct);
    }

    private async Task<AuthSessionDto> BuildSessionAsync(
        Administrator admin,
        Company company,
        string? employeeId,
        string role,
        string userName,
        CancellationToken ct,
        string? userId = null,
        string? userEmail = null)
    {
        var resolvedUserId = userId ?? "usr-" + admin.Id;
        var resolvedEmail = userEmail ?? admin.Email;

        var (token, _) = jwtTokenService.IssueToken(new JwtIssueRequest(
            resolvedUserId, userName, resolvedEmail, role, company.Id,
            AdministratorId: employeeId is null ? admin.Id : null,
            EmployeeId: employeeId));

        var companyDto = await company.ToDtoAsync(db, ct);
        var userDto = new AuthUserDto(resolvedUserId, userName, resolvedEmail, role, employeeId);
        return new AuthSessionDto(userDto, companyDto, token, DateTime.UtcNow);
    }
}
