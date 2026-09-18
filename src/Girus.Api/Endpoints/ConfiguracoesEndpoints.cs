using Girus.Application.Finance;

namespace Girus.Api.Endpoints;

public static class ConfiguracoesEndpoints
{
    public static void MapConfiguracoesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/configuracoes").WithTags("Configurações").RequireAuthorization();

        group.MapGet("/taxas", async (IFeeService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        group.MapPatch("/taxas/{id}", async (string id, SaveFeeRequest request, IFeeService service, CancellationToken ct) =>
            Results.Ok(await service.SaveAsync(request with { Id = id }, ct)));

        group.MapGet("/alertas-sanitarios", async (IAlertSettingService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(ct)));

        group.MapPut("/alertas-sanitarios", async (GlobalAlertSettingDto request, IAlertSettingService service, CancellationToken ct) =>
        {
            await service.UpdateAsync(request, ct);
            return Results.Ok();
        });
    }
}
