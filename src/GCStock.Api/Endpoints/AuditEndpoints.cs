using GCStock.Application.Audit;

namespace GCStock.Api.Endpoints;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/auditoria", async (IAuditQueryService service, CancellationToken ct) =>
                Results.Ok(await service.GetLogAsync(ct)))
            .WithTags("Auditoria")
            .RequireAuthorization();
    }
}
