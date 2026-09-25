using Girus.Application.Audit;
using Girus.Application.Abstractions;
using Girus.Application.Catalog;
using Girus.Domain.Entities;
using Girus.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace Girus.Infra.Services;

public class CategoryService(GirusDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit) : ICategoryService
{
    public async Task<List<ProductCategoryDto>> GetAllAsync(CancellationToken ct)
    {
        await ImportCategoriesFromProductsAsync(ct);
        return await QueryOrderedAsync(ct);
    }

    public async Task<ProductCategoryDto> CreateAsync(SaveCategoryRequest request, CancellationToken ct)
    {
        var name = NormalizeName(request.Name);
        if (await NameExistsAsync(name, null, ct))
            throw new InvalidOperationException($"Já existe uma categoria chamada \"{name}\".");

        var category = new ProductCategory
        {
            CompanyId = currentUser.CompanyId,
            Name = name,
            Icon = string.IsNullOrWhiteSpace(request.Icon) ? GuessIcon(name) : request.Icon.Trim(),
            SortOrder = await NextSortOrderAsync(ct),
        };
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("criou", "Categoria de Produto", category.Name, null, ct);
        return ToDto(category);
    }

    public async Task<ProductCategoryDto?> UpdateAsync(string id, SaveCategoryRequest request, CancellationToken ct)
    {
        var category = await db.ProductCategories.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == currentUser.CompanyId, ct);
        if (category is null) return null;

        var name = NormalizeName(request.Name);
        if (await NameExistsAsync(name, id, ct))
            throw new InvalidOperationException($"Já existe uma categoria chamada \"{name}\".");

        var oldName = category.Name;
        if (oldName != name)
        {
            var products = await db.Products
                .Where(p => p.CompanyId == currentUser.CompanyId && p.Category == oldName)
                .ToListAsync(ct);
            foreach (var product in products) product.Category = name;
            category.Name = name;
        }

        if (!string.IsNullOrWhiteSpace(request.Icon)) category.Icon = request.Icon.Trim();

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("editou", "Categoria de Produto", category.Name,
            oldName != name ? $"Renomeada de \"{oldName}\"" : null, ct);
        return ToDto(category);
    }

    public async Task<List<ProductCategoryDto>> ReorderAsync(ReorderCategoriesRequest request, CancellationToken ct)
    {
        var categories = await db.ProductCategories
            .Where(c => c.CompanyId == currentUser.CompanyId)
            .ToListAsync(ct);

        var position = 0;
        foreach (var id in request.Ids)
        {
            var category = categories.FirstOrDefault(c => c.Id == id);
            if (category is not null) category.SortOrder = position++;
        }

        // Categorias que não vieram na lista vão para o fim, mantendo a ordem relativa.
        foreach (var category in categories.Where(c => !request.Ids.Contains(c.Id)).OrderBy(c => c.SortOrder))
            category.SortOrder = position++;

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("editou", "Categoria de Produto", "Ordem das categorias", null, ct);
        return await QueryOrderedAsync(ct);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        var category = await db.ProductCategories.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == currentUser.CompanyId, ct);
        if (category is null) return false;

        var inUse = await db.Products.CountAsync(p => p.CompanyId == currentUser.CompanyId && p.Category == category.Name, ct);
        if (inUse > 0)
            throw new InvalidOperationException(
                $"A categoria \"{category.Name}\" está em uso por {inUse} produto(s). Troque a categoria deles antes de excluir.");

        db.ProductCategories.Remove(category);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("excluiu", "Categoria de Produto", category.Name, null, ct);
        return true;
    }

    public async Task EnsureExistsAsync(string name, CancellationToken ct)
    {
        var normalized = name.Trim();
        if (normalized.Length == 0 || await NameExistsAsync(normalized, null, ct)) return;

        db.ProductCategories.Add(new ProductCategory
        {
            CompanyId = currentUser.CompanyId,
            Name = normalized,
            Icon = GuessIcon(normalized),
            SortOrder = await NextSortOrderAsync(ct),
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Produtos criados antes do cadastro de categorias guardavam só o texto; traz esses nomes para a tabela.</summary>
    private async Task ImportCategoriesFromProductsAsync(CancellationToken ct)
    {
        var productCategories = await db.Products
            .Where(p => p.CompanyId == currentUser.CompanyId && p.Category != "")
            .Select(p => p.Category)
            .Distinct()
            .ToListAsync(ct);

        var existing = await db.ProductCategories
            .Where(c => c.CompanyId == currentUser.CompanyId)
            .Select(c => c.Name)
            .ToListAsync(ct);

        var missing = productCategories
            .Select(n => n.Trim())
            .Where(n => n.Length > 0 && !existing.Contains(n, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();
        if (missing.Count == 0) return;

        var next = await NextSortOrderAsync(ct);
        foreach (var name in missing)
        {
            db.ProductCategories.Add(new ProductCategory
            {
                CompanyId = currentUser.CompanyId,
                Name = name,
                Icon = GuessIcon(name),
                SortOrder = next++,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private Task<List<ProductCategoryDto>> QueryOrderedAsync(CancellationToken ct) =>
        db.ProductCategories
            .Where(c => c.CompanyId == currentUser.CompanyId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new ProductCategoryDto(c.Id, c.Name, c.Icon, c.SortOrder))
            .ToListAsync(ct);

    private Task<bool> NameExistsAsync(string name, string? exceptId, CancellationToken ct) =>
        db.ProductCategories.AnyAsync(
            c => c.CompanyId == currentUser.CompanyId && c.Name == name && (exceptId == null || c.Id != exceptId), ct);

    private async Task<int> NextSortOrderAsync(CancellationToken ct) =>
        (await db.ProductCategories
            .Where(c => c.CompanyId == currentUser.CompanyId)
            .MaxAsync(c => (int?)c.SortOrder, ct) ?? -1) + 1;

    private static string NormalizeName(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length == 0) throw new InvalidOperationException("Informe o nome da categoria.");
        if (normalized.Length > 100) throw new InvalidOperationException("O nome da categoria deve ter no máximo 100 caracteres.");
        return normalized;
    }

    private static string GuessIcon(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("bovin")) return "restaurant";
        if (n.Contains("suín") || n.Contains("suin") || n.Contains("porc")) return "kebab_dining";
        if (n.Contains("frango") || n.Contains("ave")) return "egg_alt";
        if (n.Contains("bebida")) return "local_bar";
        if (n.Contains("carvão") || n.Contains("carvao")) return "local_fire_department";
        return "category";
    }

    private static ProductCategoryDto ToDto(ProductCategory c) => new(c.Id, c.Name, c.Icon, c.SortOrder);
}
