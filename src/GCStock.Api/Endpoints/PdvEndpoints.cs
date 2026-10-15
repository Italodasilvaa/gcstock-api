using GCStock.Application.Access;
using GCStock.Application.Pdv;

namespace GCStock.Api.Endpoints;

public static class PdvEndpoints
{
    public static void MapPdvEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/pdv").WithTags("PDV").RequireAuthorization();

        group.MapPost("/finalizar-venda", async (FinalizeSaleRequest request, IPosService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.FinalizeSaleAsync(request, ct));
            }
            catch (UnauthorizedAccessException ex)
            {
                // Preço alterado sem a permissão "vendas.alterar-preco".
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (InvalidOperationException ex)
            {
                // Estoque insuficiente para algum item da venda.
                return Results.Conflict(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).RequireAuthorization(Permissions.SalesCreate);
    }
}
