using GCStock.Application.Access;
using GCStock.Application.Clients;

namespace GCStock.Api.Endpoints;

public static class ClientEndpoints
{
    public static void MapClientEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/clientes").WithTags("Clientes").RequireAuthorization();

        group.MapGet("/", async (IClientService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        // Histórico completo: todas as compras (qualquer pagamento), indicadores e o que mais compra.
        group.MapGet("/{id:guid}/historico", async (Guid id, IClientService service, CancellationToken ct) =>
        {
            var history = await service.GetHistoryAsync(id, ct);
            return history is not null ? Results.Ok(history) : Results.NotFound();
        });

        group.MapPost("/", async (SaveClientRequest request, IClientService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Created("/api/v1/clientes", await service.SaveAsync(request, ct));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).RequireAuthorization(Permissions.ClientsCreate);

        group.MapPut("/{id:guid}", async (Guid id, SaveClientRequest request, IClientService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.SaveAsync(request with { Id = id }, ct));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).RequireAuthorization(Permissions.ClientsEdit);

        group.MapDelete("/{id:guid}", async (Guid id, IClientService service, CancellationToken ct) =>
        {
            try
            {
                return await service.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
        }).RequireAuthorization(Permissions.ClientsDelete);
    }
}
