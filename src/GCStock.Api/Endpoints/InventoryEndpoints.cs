using GCStock.Application.Inventory;

namespace GCStock.Api.Endpoints;

public static class InventoryEndpoints
{
    public static void MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/estoque").WithTags("Estoque").RequireAuthorization();

        group.MapGet("/lotes", async (IInventoryService service, CancellationToken ct) =>
            Results.Ok(await service.GetBatchesAsync(ct)));

        group.MapGet("/movimentacoes", async (IInventoryService service, CancellationToken ct) =>
            Results.Ok(await service.GetMovementsAsync(ct)));
    }
}
