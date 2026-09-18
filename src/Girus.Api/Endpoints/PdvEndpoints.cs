using Girus.Application.Pdv;

namespace Girus.Api.Endpoints;

public static class PdvEndpoints
{
    public static void MapPdvEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/pdv").WithTags("PDV").RequireAuthorization();

        group.MapPost("/finalizar-venda", async (FinalizeSaleRequest request, IPosService service, CancellationToken ct) =>
            Results.Ok(await service.FinalizeSaleAsync(request, ct)));
    }
}
