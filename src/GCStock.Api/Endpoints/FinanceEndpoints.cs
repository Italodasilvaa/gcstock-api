using GCStock.Application.Access;
using GCStock.Application.Finance;

namespace GCStock.Api.Endpoints;

public static class FinanceEndpoints
{
    public static void MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/financeiro").WithTags("Financeiro").RequireAuthorization();

        group.MapGet("/contas-a-pagar", async (IPayableService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        group.MapPost("/contas-a-pagar", async (AddPayableRequest request, IPayableService service, CancellationToken ct) =>
            Results.Created("/api/v1/financeiro/contas-a-pagar", await service.AddAsync(request, ct))).RequireAuthorization(Permissions.PayablesCreate);

        group.MapPost("/contas-a-pagar/{id:guid}/baixar", async (Guid id, SettlePayableRequest request, IPayableService service, CancellationToken ct) =>
        {
            var settled = await service.SettleAsync(id, request, ct);
            return settled is not null ? Results.Ok(settled) : Results.NotFound();
        }).RequireAuthorization(Permissions.PayablesSettle);

        group.MapGet("/clientes-a-receber", async (IReceivableService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        group.MapPost("/clientes-a-receber", async (AddReceivableRequest request, IReceivableService service, CancellationToken ct) =>
            Results.Created("/api/v1/financeiro/clientes-a-receber", await service.AddAsync(request, ct))).RequireAuthorization(Permissions.ReceivablesCreate);

        group.MapPost("/clientes-a-receber/{id:guid}/liquidar", async (Guid id, IReceivableService service, CancellationToken ct) =>
        {
            var settled = await service.SettleAsync(id, ct);
            return settled is not null ? Results.Ok(settled) : Results.NotFound();
        }).RequireAuthorization(Permissions.ReceivablesSettle);

        group.MapGet("/liquidacoes-cartao", async (ICardSettlementService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        group.MapPost("/liquidacoes-cartao/{id:guid}/antecipar", async (Guid id, ICardSettlementService service, CancellationToken ct) =>
        {
            await service.AnticipateAsync(id, ct);
            return Results.Ok();
        }).RequireAuthorization(Permissions.CardsAnticipate);

        // Situação financeira do período (entradas, vendas, custo, lucro, despesas e resultado). Padrão: mês atual.
        group.MapGet("/resultado", async (DateOnly? de, DateOnly? ate, IFinancialSummaryService service, CancellationToken ct) =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3)); // data em Brasília
            var from = de ?? new DateOnly(today.Year, today.Month, 1);
            var to = ate ?? from.AddMonths(1).AddDays(-1);
            return Results.Ok(await service.GetAsync(from, to, ct));
        });

        group.MapGet("/vendas", async (ISalesHistoryService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));
    }
}
