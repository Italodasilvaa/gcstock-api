using Girus.Application.Catalog;

namespace Girus.Api.Endpoints;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/api/v1/produtos").WithTags("Produtos").RequireAuthorization();

        products.MapGet("/", async (IProductService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        products.MapPost("/", async (SaveProductRequest request, IProductService service, CancellationToken ct) =>
            Results.Created("/api/v1/produtos", await service.SaveAsync(request, ct)));

        products.MapPut("/{id}", async (string id, SaveProductRequest request, IProductService service, CancellationToken ct) =>
            Results.Ok(await service.SaveAsync(request with { Id = id }, ct)));

        products.MapDelete("/{id}", async (string id, IProductService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        var kits = app.MapGroup("/api/v1/kits-churrasco").WithTags("Kits").RequireAuthorization();

        kits.MapGet("/", async (IKitService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        kits.MapPost("/", async (SaveKitRequest request, IKitService service, CancellationToken ct) =>
            Results.Created("/api/v1/kits-churrasco", await service.SaveAsync(request, ct)));

        kits.MapPut("/{id}", async (string id, SaveKitRequest request, IKitService service, CancellationToken ct) =>
            Results.Ok(await service.SaveAsync(request with { Id = id }, ct)));
    }
}
