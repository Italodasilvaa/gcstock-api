using System.Security.Claims;
using GCStock.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace GCStock.Infra.Security;

public class HttpCurrentUserContext(IHttpContextAccessor accessor) : ICurrentUserContext
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public Guid UserId => ParseGuid(User?.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub))
        ?? throw new InvalidOperationException("Usuário não autenticado.");

    public string UserName => User?.FindFirstValue(ClaimTypes.Name) ?? string.Empty;

    public string UserEmail => User?.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

    public string Role => User?.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

    public Guid CompanyId => ParseGuid(User?.FindFirstValue(ClaimTypesGCStock.CompanyId))
        ?? throw new InvalidOperationException("Token sem empresa associada.");

    public Guid? AdministratorId => ParseGuid(User?.FindFirstValue(ClaimTypesGCStock.AdministratorId));

    public Guid? EmployeeId => ParseGuid(User?.FindFirstValue(ClaimTypesGCStock.EmployeeId));

    public bool IsOwner => AdministratorId.HasValue;

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
