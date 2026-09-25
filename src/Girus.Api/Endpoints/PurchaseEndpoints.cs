using Girus.Application.Purchasing;

namespace Girus.Api.Endpoints;

public static class PurchaseEndpoints
{
    public static void MapPurchaseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/compras").WithTags("Compras").RequireAuthorization();

        group.MapGet("/", async (IPurchaseService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        group.MapPost("/entrada", async (SavePurchaseRequest request, IPurchaseService service, CancellationToken ct) =>
            Results.Created("/api/v1/compras", await service.SaveAsync(request, ct)));

        group.MapPut("/{id:guid}", async (Guid id, SavePurchaseRequest request, IPurchaseService service, CancellationToken ct) =>
        {
            try
            {
                var updated = await service.UpdateAsync(id, request, ct);
                return updated is not null ? Results.Ok(updated) : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                // Compra já paga: não pode mais ser alterada.
                return Results.Conflict(new { message = ex.Message });
            }
        });
    }
}
