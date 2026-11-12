using GCStock.Application.Auth;
using GCStock.Infra.Security;

namespace GCStock.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/register-company", async (RegisterCompanyRequest request, IAuthService service, CancellationToken ct) =>
        {
            try
            {
                var session = await service.RegisterCompanyAsync(request, ct);
                return Results.Created($"/api/v1/tenants/my-companies", session);
            }
            catch (LoginLockedException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status429TooManyRequests);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
        }).AllowAnonymous();

        group.MapPost("/login", async (LoginRequest request, IAuthService service, CancellationToken ct) =>
        {
            try
            {
                var session = await service.LoginAsync(request, ct);
                return session is not null ? Results.Ok(session) : Results.Unauthorized();
            }
            catch (LoginLockedException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status429TooManyRequests);
            }
        }).AllowAnonymous();

        group.MapPost("/logout", () => Results.Ok()).RequireAuthorization();

        group.MapGet("/me", async (IAuthService service, CancellationToken ct) =>
        {
            var session = await service.GetCurrentSessionAsync(ct);
            return session is not null ? Results.Ok(session) : Results.Unauthorized();
        }).RequireAuthorization();

        // Emite um novo token para o usuário autenticado — usado pelo aviso de inatividade do front-end
        // para manter a sessão viva sem exigir login de novo.
        group.MapPost("/renew", async (IAuthService service, CancellationToken ct) =>
        {
            var session = await service.RenewTokenAsync(ct);
            return session is not null ? Results.Ok(session) : Results.Unauthorized();
        }).RequireAuthorization();
    }
}
