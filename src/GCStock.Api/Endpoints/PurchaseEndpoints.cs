using GCStock.Application.Access;
using GCStock.Application.Purchasing;

namespace GCStock.Api.Endpoints;

public static class PurchaseEndpoints
{
    public static void MapPurchaseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/compras").WithTags("Compras").RequireAuthorization();

        group.MapGet("/", async (IPurchaseService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        group.MapPost("/entrada", async (SavePurchaseRequest request, IPurchaseService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Created("/api/v1/compras", await service.SaveAsync(request, ct));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).RequireAuthorization(Permissions.PurchasesCreate);

        group.MapDelete("/{id:guid}", async (Guid id, IPurchaseService service, CancellationToken ct) =>
        {
            try
            {
                return await service.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
        }).RequireAuthorization(Permissions.PurchasesDelete);

        group.MapPut("/{id:guid}", async (Guid id, SavePurchaseRequest request, IPurchaseService service, CancellationToken ct) =>
        {
            try
            {
                var updated = await service.UpdateAsync(id, request, ct);
                return updated is not null ? Results.Ok(updated) : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
        }).RequireAuthorization(Permissions.PurchasesEdit); // editar pedido exige a permissão "compras.editar" (dono tem sempre)
    }
}
