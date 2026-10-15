using GCStock.Application.Access;

namespace GCStock.Api.Endpoints;

public static class PermissionEndpoints
{
    public static void MapPermissionEndpoints(this IEndpointRouteBuilder app)
    {
        // Catálogo das permissões configuráveis por funcionário (usado pelo cadastro de funcionários no front).
        app.MapGet("/api/v1/permissoes", () => Results.Ok(Permissions.All))
            .WithTags("Permissões")
            .RequireAuthorization();
    }
}
