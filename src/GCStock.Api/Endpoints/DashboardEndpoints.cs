using GCStock.Application.Dashboard;

namespace GCStock.Api.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dashboard").WithTags("Dashboard").RequireAuthorization();

        // Indicadores comerciais, financeiros, de estoque e de kits, de dados reais. Padrão: hoje (horário de Brasília).
        group.MapGet("/", async (DateOnly? referencia, IDashboardService service, CancellationToken ct) =>
        {
            var reference = referencia ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
            return Results.Ok(await service.GetAsync(reference, ct));
        });
    }
}
