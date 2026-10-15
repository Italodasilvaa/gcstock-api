using GCStock.Application.Access;
using GCStock.Application.Sales;

namespace GCStock.Api.Endpoints;

public static class SalesEndpoints
{
    public static void MapSalesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/vendas").WithTags("Vendas").RequireAuthorization();

        // Histórico de vendas do PDV, com itens, preços e forma de pagamento (inclui as canceladas).
        group.MapGet("/", async (ISaleService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        // Cancelamento (estorno): devolve o estoque e desfaz o recebível. Exige a permissão "vendas.cancelar".
        group.MapPost("/{saleId:guid}/cancelar", async (Guid saleId, CancelSaleRequest request, ISaleService service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.CancelAsync(saleId, request.Reason, ct);
                return result is not null ? Results.Ok(result) : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
        }).RequireAuthorization(Permissions.SalesCancel);
    }
}
