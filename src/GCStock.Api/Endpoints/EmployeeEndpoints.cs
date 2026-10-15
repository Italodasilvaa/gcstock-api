using GCStock.Application.Access;
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
            await SaveAsync(() => service.SaveAsync(request, ct), created: true)).RequireAuthorization(Permissions.EmployeesCreate);

        group.MapPut("/{id:guid}", async (Guid id, SaveEmployeeRequest request, IEmployeeService service, CancellationToken ct) =>
            await SaveAsync(() => service.SaveAsync(request with { Id = id }, ct), created: false)).RequireAuthorization(Permissions.EmployeesEdit);

        group.MapDelete("/{id:guid}", async (Guid id, IEmployeeService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(Permissions.EmployeesDelete);

        group.MapPost("/{id:guid}/transferir", async (Guid id, TransferEmployeeRequest request, IEmployeeService service, CancellationToken ct) =>
        {
            var updated = await service.TransferAsync(id, request.TargetCompanyId, ct);
            return updated is not null ? Results.Ok(updated) : Results.BadRequest(new { message = "Transferência não permitida." });
        });
    }

    private static async Task<IResult> SaveAsync(Func<Task<EmployeeDto>> save, bool created)
    {
        try
        {
            var employee = await save();
            return created ? Results.Created("/api/v1/funcionarios", employee) : Results.Ok(employee);
        }
        catch (UnauthorizedAccessException ex)
        {
            // Ex.: funcionário tentando definir permissões — só o dono pode.
            return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}

public record TransferEmployeeRequest(Guid TargetCompanyId);
