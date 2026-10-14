using GCStock.Application.Employees;

namespace GCStock.Api.Endpoints;

public static class EmployeeEndpoints
{
    public static void MapEmployeeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/funcionarios").WithTags("Funcionários").RequireAuthorization();

        group.MapGet("/", async (IEmployeeService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        group.MapPost("/", async (SaveEmployeeRequest request, IEmployeeService service, CancellationToken ct) =>
            Results.Created($"/api/v1/funcionarios", await service.SaveAsync(request, ct)));

        group.MapPut("/{id:guid}", async (Guid id, SaveEmployeeRequest request, IEmployeeService service, CancellationToken ct) =>
            Results.Ok(await service.SaveAsync(request with { Id = id }, ct)));

        group.MapDelete("/{id:guid}", async (Guid id, IEmployeeService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/transferir", async (Guid id, TransferEmployeeRequest request, IEmployeeService service, CancellationToken ct) =>
        {
            var updated = await service.TransferAsync(id, request.TargetCompanyId, ct);
            return updated is not null ? Results.Ok(updated) : Results.BadRequest(new { message = "Transferência não permitida." });
        });
    }
}

public record TransferEmployeeRequest(Guid TargetCompanyId);
