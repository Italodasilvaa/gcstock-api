using GCStock.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace GCStock.Api.Authorization;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>Autoriza a rota quando o usuário (dono ou funcionário) possui a permissão exigida.</summary>
public sealed class PermissionAuthorizationHandler(IPermissionChecker permissions)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (await permissions.HasAsync(requirement.Permission, CancellationToken.None))
        {
            context.Succeed(requirement);
        }
    }
}
