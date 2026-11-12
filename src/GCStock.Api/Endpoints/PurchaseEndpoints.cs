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

        // Compra nunca é apagada: o cancelamento estorna estoque/lotes por movimentações inversas e guarda quem, quando e por quê.
        group.MapPost("/{id:guid}/cancelar", async (Guid id, CancelPurchaseRequest request, IPurchaseService service, CancellationToken ct) =>
        {
            try
            {
                var cancelled = await service.CancelAsync(id, request.Reason, ct);
                return cancelled is not null ? Results.Ok(cancelled) : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
        }).RequireAuthorization(Permissions.PurchasesDelete);

        // Dinheiro devolvido pelo fornecedor por uma compra paga e cancelada.
        group.MapPost("/{id:guid}/devolucoes", async (Guid id, RegisterPurchaseRefundRequest request, IPurchaseService service, CancellationToken ct) =>
        {
            try
            {
                var updated = await service.RegisterRefundAsync(id, request, ct);
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
        }).RequireAuthorization(Permissions.PurchasesEdit);

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
