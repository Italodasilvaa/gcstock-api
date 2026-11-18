using GCStock.Application.Tenants;

namespace GCStock.Api.Endpoints;

public static class TenantEndpoints
{
    public static void MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tenants").WithTags("Tenants").RequireAuthorization();

        group.MapGet("/my-companies", async (ITenantService service, CancellationToken ct) =>
            Results.Ok(await service.GetMyCompaniesAsync(ct)));

        group.MapPost("/switch", async (SwitchCompanyRequest request, ITenantService service, CancellationToken ct) =>
        {
            var session = await service.SwitchCompanyAsync(request.CompanyId, ct);
            return session is not null ? Results.Ok(session) : Results.BadRequest(new { message = "Troca de empresa não permitida." });
        });

        // Dados cadastrais da empresa ativa: só o dono (validado no serviço).
        group.MapPut("/current", async (UpdateCompanyRequest request, ITenantService service, CancellationToken ct) =>
        {
            try
            {
                var company = await service.UpdateCurrentCompanyAsync(request, ct);
                return company is not null
                    ? Results.Ok(company)
                    : Results.Json(new { message = "Só o dono pode alterar os dados da empresa." }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapGet("/filiais", async (ITenantService service, CancellationToken ct) =>
            Results.Ok(await service.GetBranchesAsync(ct)));
    }
}

public record SwitchCompanyRequest(Guid CompanyId);
