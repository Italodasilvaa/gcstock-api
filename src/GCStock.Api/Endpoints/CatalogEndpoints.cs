using GCStock.Application.Catalog;

namespace GCStock.Api.Endpoints;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/api/v1/produtos").WithTags("Produtos").RequireAuthorization();

        products.MapGet("/", async (IProductService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        products.MapPost("/", async (SaveProductRequest request, IProductService service, CancellationToken ct) =>
            Results.Created("/api/v1/produtos", await service.SaveAsync(request, ct)));

        products.MapPut("/{id:guid}", async (Guid id, SaveProductRequest request, IProductService service, CancellationToken ct) =>
            Results.Ok(await service.SaveAsync(request with { Id = id }, ct)));

        products.MapDelete("/{id:guid}", async (Guid id, IProductService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        var categories = app.MapGroup("/api/v1/categorias-produto").WithTags("Categorias de Produto").RequireAuthorization();

        categories.MapGet("/", async (ICategoryService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        categories.MapPost("/", async (SaveCategoryRequest request, ICategoryService service, CancellationToken ct) =>
        {
            try
            {
                var created = await service.CreateAsync(request, ct);
                return Results.Created($"/api/v1/categorias-produto/{created.Id}", created);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        categories.MapPut("/ordem", async (ReorderCategoriesRequest request, ICategoryService service, CancellationToken ct) =>
            Results.Ok(await service.ReorderAsync(request, ct)));

        categories.MapPut("/{id:guid}", async (Guid id, SaveCategoryRequest request, ICategoryService service, CancellationToken ct) =>
        {
            try
            {
                var updated = await service.UpdateAsync(id, request, ct);
                return updated is not null ? Results.Ok(updated) : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        categories.MapDelete("/{id:guid}", async (Guid id, ICategoryService service, CancellationToken ct) =>
        {
            try
            {
                return await service.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
        });

        var kits = app.MapGroup("/api/v1/kits-churrasco").WithTags("Kits").RequireAuthorization();

        kits.MapGet("/", async (IKitService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        kits.MapPost("/", async (SaveKitRequest request, IKitService service, CancellationToken ct) =>
            Results.Created("/api/v1/kits-churrasco", await service.SaveAsync(request, ct)));

        kits.MapPut("/{id:guid}", async (Guid id, SaveKitRequest request, IKitService service, CancellationToken ct) =>
            Results.Ok(await service.SaveAsync(request with { Id = id }, ct)));

        kits.MapDelete("/{id:guid}", async (Guid id, IKitService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });
    }
}
