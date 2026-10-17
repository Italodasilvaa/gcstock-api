using GCStock.Application.Access;
using GCStock.Application.Finance;

namespace GCStock.Api.Endpoints;

public static class ConfiguracoesEndpoints
{
    public static void MapConfiguracoesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/configuracoes").WithTags("Configurações").RequireAuthorization();

        group.MapGet("/taxas", async (IFeeService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        group.MapPost("/taxas", async (CreateFeeRequest request, IFeeService service, CancellationToken ct) =>
        {
            try
            {
                var created = await service.CreateAsync(request, ct);
                return Results.Created($"/api/v1/configuracoes/taxas/{created.Id}", created);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).RequireAuthorization(Permissions.FeesCreate);

        group.MapDelete("/taxas/{id:guid}", async (Guid id, IFeeService service, CancellationToken ct) =>
            await service.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound()).RequireAuthorization(Permissions.FeesDelete);

        group.MapPut("/taxas/ordem", async (ReorderFeesRequest request, IFeeService service, CancellationToken ct) =>
            Results.Ok(await service.ReorderAsync(request, ct))).RequireAuthorization(Permissions.FeesEdit);

        group.MapPatch("/taxas/{id:guid}", async (Guid id, SaveFeeRequest request, IFeeService service, CancellationToken ct) =>
            Results.Ok(await service.SaveAsync(request with { Id = id }, ct))).RequireAuthorization(Permissions.FeesEdit);

        group.MapGet("/alertas-sanitarios", async (IAlertSettingService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(ct)));

        group.MapPut("/alertas-sanitarios", async (GlobalAlertSettingDto request, IAlertSettingService service, CancellationToken ct) =>
        {
            await service.UpdateAsync(request, ct);
            return Results.Ok();
        }).RequireAuthorization(Permissions.AlertsEdit);
    }
}
