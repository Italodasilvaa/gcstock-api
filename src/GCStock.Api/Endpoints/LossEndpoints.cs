using GCStock.Application.Access;
using GCStock.Application.Inventory;

namespace GCStock.Api.Endpoints;

public static class LossEndpoints
{
    public static void MapLossEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/estoque/perdas").WithTags("Perdas de estoque").RequireAuthorization();

        group.MapGet("/", async (IStockLossService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        // Registra uma perda (vencimento, quebra, furto...) com o custo informado: abate o estoque e grava o prejuízo.
        group.MapPost("/", async (CreateStockLossRequest request, IStockLossService service, CancellationToken ct) =>
        {
            try
            {
                var created = await service.CreateAsync(request, ct);
                return Results.Created($"/api/v1/estoque/perdas/{created.Id}", created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                // Quantidade maior que o estoque (ou que o lote escolhido).
                return Results.Conflict(new { message = ex.Message });
            }
        }).RequireAuthorization(Permissions.LossesCreate);

        // Desfaz uma perda lançada por engano: devolve ao estoque e tira o prejuízo do resultado.
        group.MapDelete("/{id:guid}", async (Guid id, IStockLossService service, CancellationToken ct) =>
        {
            try
            {
                var undone = await service.UndoAsync(id, ct);
                return undone is not null ? Results.Ok(undone) : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
        }).RequireAuthorization(Permissions.LossesUndo);
    }
}
