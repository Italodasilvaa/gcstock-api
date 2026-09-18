using Girus.Application.Finance;

namespace Girus.Api.Endpoints;

public static class FinanceEndpoints
{
    public static void MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/financeiro").WithTags("Financeiro").RequireAuthorization();

        group.MapGet("/contas-a-pagar", async (IPayableService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        group.MapPost("/contas-a-pagar", async (AddPayableRequest request, IPayableService service, CancellationToken ct) =>
            Results.Created("/api/v1/financeiro/contas-a-pagar", await service.AddAsync(request, ct)));

        group.MapPost("/contas-a-pagar/{id}/baixar", async (string id, SettlePayableRequest request, IPayableService service, CancellationToken ct) =>
        {
            var settled = await service.SettleAsync(id, request, ct);
            return settled is not null ? Results.Ok(settled) : Results.NotFound();
        });

        group.MapGet("/clientes-a-receber", async (IReceivableService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        group.MapPost("/clientes-a-receber", async (AddReceivableRequest request, IReceivableService service, CancellationToken ct) =>
            Results.Created("/api/v1/financeiro/clientes-a-receber", await service.AddAsync(request, ct)));

        group.MapPost("/clientes-a-receber/{id}/liquidar", async (string id, IReceivableService service, CancellationToken ct) =>
        {
            var settled = await service.SettleAsync(id, ct);
            return settled is not null ? Results.Ok(settled) : Results.NotFound();
        });

        group.MapGet("/liquidacoes-cartao", async (ICardSettlementService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        group.MapPost("/liquidacoes-cartao/{id}/antecipar", async (string id, ICardSettlementService service, CancellationToken ct) =>
        {
            await service.AnticipateAsync(id, ct);
            return Results.Ok();
        });

        group.MapGet("/vendas", async (ISalesHistoryService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));
    }
}
