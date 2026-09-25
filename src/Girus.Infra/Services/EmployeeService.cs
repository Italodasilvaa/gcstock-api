using Girus.Application.Audit;
using Girus.Application.Abstractions;
using Girus.Application.Employees;
using Girus.Domain.Entities;
using Girus.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace Girus.Infra.Services;

public class EmployeeService(GirusDbContext db, ICurrentUserContext currentUser, IPasswordHasher passwordHasher, IAuditRecorder audit)
    : IEmployeeService
{
    public async Task<List<EmployeeDto>> GetAllAsync(CancellationToken ct)
    {
        return await db.Employees
            .Where(e => e.CompanyId == currentUser.CompanyId)
            .Select(e => ToDto(e))
            .ToListAsync(ct);
    }

    public async Task<EmployeeDto> SaveAsync(SaveEmployeeRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(request.Id))
        {
            var existing = await db.Employees.FirstOrDefaultAsync(e => e.Id == request.Id && e.CompanyId == currentUser.CompanyId, ct);
            if (existing is not null)
            {
                existing.Name = request.Name;
                existing.Email = request.Email;
                if (!string.IsNullOrWhiteSpace(request.Password)) existing.PasswordHash = passwordHasher.Hash(request.Password);
                if (request.Role is not null) existing.Role = request.Role;
                if (request.Active is not null) existing.Active = request.Active.Value;
                if (request.Salary is not null) existing.Salary = request.Salary.Value;

                await db.SaveChangesAsync(ct);
                await audit.RecordAsync("editou", "Funcionário", existing.Name, null, ct);
                return ToDto(existing);
            }
        }

        var employee = new Employee
        {
            CompanyId = currentUser.CompanyId,
            Name = request.Name,
            Email = request.Email,
            PasswordHash = passwordHasher.Hash(string.IsNullOrWhiteSpace(request.Password) ? "123456" : request.Password),
            Role = string.IsNullOrWhiteSpace(request.Role) ? "operador" : request.Role,
            Active = request.Active ?? true,
            Salary = request.Salary ?? 0,
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("criou", "Funcionário", employee.Name, null, ct);
        return ToDto(employee);
    }

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        var target = await db.Employees.FirstOrDefaultAsync(e => e.Id == id && e.CompanyId == currentUser.CompanyId, ct);
        if (target is null) return;

        db.Employees.Remove(target);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("excluiu", "Funcionário", target.Name, null, ct);
    }

    public async Task<EmployeeDto?> TransferAsync(string employeeId, string targetCompanyId, CancellationToken ct)
    {
        if (!currentUser.IsOwner) return null;

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null) return null;

        var sourceCompany = await db.Companies.FirstOrDefaultAsync(c => c.Id == employee.CompanyId, ct);
        var targetCompany = await db.Companies.FirstOrDefaultAsync(c => c.Id == targetCompanyId, ct);
        if (sourceCompany is null || targetCompany is null || sourceCompany.OwnerId != targetCompany.OwnerId) return null;

        employee.CompanyId = targetCompanyId;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(
            "editou", "Funcionário", employee.Name,
            $"Transferido de {sourceCompany.TradeName} para {targetCompany.TradeName}", ct);
        return ToDto(employee);
    }

    private static EmployeeDto ToDto(Employee e) => new(
        e.Id, e.CompanyId, e.Name, e.Email, e.Role, e.Active, DateOnly.FromDateTime(e.CreatedAt), e.Salary);
}
