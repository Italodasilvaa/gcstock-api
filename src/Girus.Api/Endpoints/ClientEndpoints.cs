using Girus.Application.Clients;

namespace Girus.Api.Endpoints;

public static class ClientEndpoints
{
    public static void MapClientEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/clientes").WithTags("Clientes").RequireAuthorization();

        group.MapGet("/", async (IClientService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        group.MapPost("/", async (SaveClientRequest request, IClientService service, CancellationToken ct) =>
            Results.Created("/api/v1/clientes", await service.SaveAsync(request, ct)));

        group.MapPut("/{id}", async (string id, SaveClientRequest request, IClientService service, CancellationToken ct) =>
            Results.Ok(await service.SaveAsync(request with { Id = id }, ct)));
    }
}
