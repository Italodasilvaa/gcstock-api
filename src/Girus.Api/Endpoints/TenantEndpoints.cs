using Girus.Application.Tenants;

namespace Girus.Api.Endpoints;

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

        group.MapGet("/filiais", async (ITenantService service, CancellationToken ct) =>
            Results.Ok(await service.GetBranchesAsync(ct)));
    }
}

public record SwitchCompanyRequest(string CompanyId);
