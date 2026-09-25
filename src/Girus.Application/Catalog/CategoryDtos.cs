namespace Girus.Application.Catalog;

public record ProductCategoryDto(string Id, string Name, string Icon, int SortOrder);

public record SaveCategoryRequest(string Name, string? Icon);

public record ReorderCategoriesRequest(List<string> Ids);

public interface ICategoryService
{
    /// <summary>Lista as categorias na ordem definida, incluindo as que só existiam como texto nos produtos.</summary>
    Task<List<ProductCategoryDto>> GetAllAsync(CancellationToken ct);
    Task<ProductCategoryDto> CreateAsync(SaveCategoryRequest request, CancellationToken ct);
    /// <summary>Renomear também atualiza a categoria dos produtos que usavam o nome antigo.</summary>
    Task<ProductCategoryDto?> UpdateAsync(string id, SaveCategoryRequest request, CancellationToken ct);
    Task<List<ProductCategoryDto>> ReorderAsync(ReorderCategoriesRequest request, CancellationToken ct);
    /// <summary>Retorna false quando a categoria não existe; lança InvalidOperationException se houver produtos usando.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct);
    /// <summary>Garante que a categoria exista (usado ao salvar um produto com categoria nova).</summary>
    Task EnsureExistsAsync(string name, CancellationToken ct);
}
