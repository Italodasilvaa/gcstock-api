using System.Security.Claims;
using Girus.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Girus.Infra.Security;

public class HttpCurrentUserContext(IHttpContextAccessor accessor) : ICurrentUserContext
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public string UserId => User?.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException("Usuário não autenticado.");

    public string UserName => User?.FindFirstValue(ClaimTypes.Name) ?? string.Empty;

    public string UserEmail => User?.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

    public string Role => User?.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

    public string CompanyId => User?.FindFirstValue(ClaimTypesGirus.CompanyId)
        ?? throw new InvalidOperationException("Token sem empresa associada.");

    public string? AdministratorId => User?.FindFirstValue(ClaimTypesGirus.AdministratorId);

    public string? EmployeeId => User?.FindFirstValue(ClaimTypesGirus.EmployeeId);

    public bool IsOwner => !string.IsNullOrEmpty(AdministratorId);
}
