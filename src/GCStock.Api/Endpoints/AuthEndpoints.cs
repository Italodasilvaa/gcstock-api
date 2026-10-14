using GCStock.Application.Auth;

namespace GCStock.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/register-company", async (RegisterCompanyRequest request, IAuthService service, CancellationToken ct) =>
        {
            var session = await service.RegisterCompanyAsync(request, ct);
            return Results.Created($"/api/v1/tenants/my-companies", session);
        }).AllowAnonymous();

        group.MapPost("/login", async (LoginRequest request, IAuthService service, CancellationToken ct) =>
        {
            var session = await service.LoginAsync(request, ct);
            return session is not null ? Results.Ok(session) : Results.Unauthorized();
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

        // Lista pública (dados não sensíveis) para a tela de login exibir as empresas de demonstração.
        group.MapGet("/companies", async (IAuthService service, CancellationToken ct) =>
            Results.Ok(await service.ListCompaniesForLoginAsync(ct))).AllowAnonymous();

        // Conveniência de "acesso rápido" da tela de login (ambiente de demonstração).
        // Expõe o e-mail do dono de uma empresa sem autenticação — recomenda-se remover
        // ou proteger este endpoint antes de qualquer uso em produção real.
        group.MapGet("/companies/{companyId:guid}/quick-login-email", async (Guid companyId, IAuthService service, CancellationToken ct) =>
        {
            var email = await service.GetCompanyOwnerEmailAsync(companyId, ct);
            return email is not null ? Results.Ok(new { email }) : Results.NotFound();
        }).AllowAnonymous();
    }
}
