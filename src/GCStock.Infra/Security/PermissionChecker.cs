using GCStock.Application.Abstractions;
using GCStock.Application.Access;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Security;

public class PermissionChecker(GCStockDbContext db, ICurrentUserContext currentUser) : IPermissionChecker
{
    public async Task<bool> HasAsync(string permission, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return false;
        if (currentUser.IsOwner) return true;

        var granted = await GetCurrentAsync(ct);
        return granted.Contains(permission);
    }

    public async Task<List<string>> GetCurrentAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return [];
        if (currentUser.IsOwner) return [.. Permissions.AllKeys];
        if (currentUser.EmployeeId is not { } employeeId) return [];

        // Funcionário inativo ou transferido de empresa perde as permissões.
        return await db.Employees
            .Where(e => e.Id == employeeId && e.CompanyId == currentUser.CompanyId && e.Active)
            .Select(e => e.Permissions)
            .FirstOrDefaultAsync(ct) ?? [];
    }
}
