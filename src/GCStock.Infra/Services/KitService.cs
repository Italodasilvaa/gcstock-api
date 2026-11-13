using GCStock.Application.Audit;
using GCStock.Application.Abstractions;
using GCStock.Application.Catalog;
using GCStock.Domain.Entities;
using GCStock.Domain.Services;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

public class KitService(GCStockDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit) : IKitService
{
    /// <summary>Margem mínima padrão quando a empresa ainda não tem configuração de alertas.</summary>
    private const decimal DefaultMinMarginPercent = 30m;

    public async Task<List<KitDto>> GetAllAsync(CancellationToken ct)
    {
        var kits = await db.Kits
            .Include(k => k.Components)
            .Where(k => k.CompanyId == currentUser.CompanyId)
            .ToListAsync(ct);
        var companyMin = await CompanyMinMarginAsync(ct);
        return kits.Select(k => ToDto(k, companyMin)).ToList();
    }

    public async Task<KitDto> SaveAsync(SaveKitRequest request, CancellationToken ct)
    {
        // Componentes só podem apontar para produtos da própria empresa (o ID vem do cliente).
        var requested = request.Components;
        var componentProductIds = requested?.Select(c => c.ProductId).Distinct().ToList() ?? [];
        if (requested is not null && requested.Any(c => c.Quantity <= 0))
        {
            throw new ArgumentException("Todos os componentes do kit precisam de quantidade maior que zero.");
        }

        Dictionary<Guid, Product> requestedProducts = [];
        if (componentProductIds.Count > 0)
        {
            requestedProducts = await db.Products
                .Where(p => p.CompanyId == currentUser.CompanyId && componentProductIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);
            if (requestedProducts.Count != componentProductIds.Count)
            {
                throw new ArgumentException("Um dos produtos da composição do kit não foi encontrado.");
            }
        }

        if (request.Id.HasValue)
        {
            var existing = await db.Kits
                .Include(k => k.Components)
                .FirstOrDefaultAsync(k => k.Id == request.Id && k.CompanyId == currentUser.CompanyId, ct);
            if (existing is not null)
            {
                var previousCost = existing.CostPrice;
                var previousMargin = existing.MarginPercent;

                if (request.Name is not null) existing.Name = request.Name;
                if (request.Tag is not null) existing.Tag = request.Tag;
                if (request.ServeLabel is not null) existing.ServeLabel = request.ServeLabel;
                if (request.Description is not null) existing.Description = request.Description;
                if (request.SuggestedPrice is not null) existing.SuggestedPrice = request.SuggestedPrice.Value;
                if (request.TotalWeightKg is not null) existing.TotalWeightKg = request.TotalWeightKg.Value;
                if (request.ImageUrl is not null) existing.ImageUrl = request.ImageUrl;
                if (request.ComponentsSummary is not null) existing.ComponentsSummary = request.ComponentsSummary;
                if (request.StockAvailable is not null) existing.StockAvailable = request.StockAvailable.Value;
                // A tela sempre envia a margem mínima do kit (vazia = usar a da empresa).
                existing.MinMarginPercent = ValidMinMargin(request.MinMarginPercent);
                if (requested is null && request.CostPrice is not null && existing.Components.Count == 0)
                {
                    existing.CostPrice = request.CostPrice.Value;
                }

                if (requested is not null)
                {
                    db.KitComponents.RemoveRange(existing.Components);
                    var rebuilt = requested.Select(c => NewComponent(c, requestedProducts)).ToList();
                    existing.Components = rebuilt;
                    // O Id do componente é gerado no construtor; sem o Add explícito o EF o trata como já existente e
                    // tenta UPDATE (DbUpdateConcurrencyException: 0 linhas afetadas). Add marca como inserção.
                    db.KitComponents.AddRange(rebuilt);
                }

                // Custo, margem e peso são do servidor: vêm do custo médio atual dos produtos, nunca do que a tela enviou.
                var products = await ProductsOfAsync(existing, requestedProducts, ct);
                KitPricing.Recalculate(existing, products);
                RecordHistory(existing, previousCost, previousMargin, "edicao_kit");

                await db.SaveChangesAsync(ct);
                await audit.RecordAsync("editou", "Kit de Churrasco", existing.Name, null, ct);
                return ToDto(existing, await CompanyMinMarginAsync(ct));
            }
        }

        var count = await db.Kits.CountAsync(k => k.CompanyId == currentUser.CompanyId, ct);
        var saved = new Kit
        {
            CompanyId = currentUser.CompanyId,
            Code = "KIT-00" + (count + 1),
            Name = request.Name ?? "Novo Kit de Churrasco",
            Tag = request.Tag ?? "Combo Especial",
            ServeLabel = request.ServeLabel ?? "Serve 6 a 8 pessoas",
            Description = request.Description ?? "Cortes selecionados para churrasco.",
            SuggestedPrice = request.SuggestedPrice ?? 299.0m,
            // Sem componentes não há de onde calcular: vale o informado. Com componentes, o servidor recalcula abaixo.
            CostPrice = request.CostPrice ?? 0m,
            TotalWeightKg = request.TotalWeightKg ?? 3.5m,
            ImageUrl = request.ImageUrl ?? string.Empty,
            ComponentsSummary = request.ComponentsSummary ?? "Picanha Angus, Ancho, Pão de Alho.",
            StockAvailable = request.StockAvailable ?? 10,
            StockStatus = "disponivel",
            BarcodeEan = "78994582000" + Random.Shared.Next(10, 100),
            MinMarginPercent = ValidMinMargin(request.MinMarginPercent),
            Components = requested?.Select(c => NewComponent(c, requestedProducts)).ToList() ?? [],
        };
        KitPricing.Recalculate(saved, requestedProducts);
        db.Kits.Add(saved);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("criou", "Kit de Churrasco", saved.Name, null, ct);
        return ToDto(saved, await CompanyMinMarginAsync(ct));
    }

    public async Task<List<KitCostHistoryDto>> GetCostHistoryAsync(Guid kitId, CancellationToken ct)
    {
        var rows = await db.KitCostHistories
            .Where(h => h.CompanyId == currentUser.CompanyId && h.KitId == kitId)
            .OrderByDescending(h => h.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(h => new KitCostHistoryDto(
            h.Id, h.PreviousCost, h.NewCost, h.PreviousMarginPercent, h.NewMarginPercent, h.SuggestedPrice,
            h.Source, h.TriggerProduct, h.CreatedAt, h.CreatedBy)).ToList();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var target = await db.Kits.FirstOrDefaultAsync(k => k.Id == id && k.CompanyId == currentUser.CompanyId, ct);
        if (target is null) return;

        db.Kits.Remove(target);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("excluiu", "Kit de Churrasco", target.Name, null, ct);
    }

    private static decimal? ValidMinMargin(decimal? value)
    {
        if (value is null) return null;
        if (value < 0 || value >= 100) throw new ArgumentException("A margem mínima deve ficar entre 0 e 99,99%.");
        return Math.Round(value.Value, 2, MidpointRounding.AwayFromZero);
    }

    private async Task<decimal> CompanyMinMarginAsync(CancellationToken ct) =>
        await db.AlertSettings
            .Where(a => a.CompanyId == currentUser.CompanyId)
            .Select(a => (decimal?)a.MinKitMarginPercent)
            .FirstOrDefaultAsync(ct) ?? DefaultMinMarginPercent;

    /// <summary>Produtos dos componentes do kit (os já carregados na requisição + os demais, lidos do banco).</summary>
    private async Task<Dictionary<Guid, Product>> ProductsOfAsync(Kit kit, Dictionary<Guid, Product> alreadyLoaded, CancellationToken ct)
    {
        var products = new Dictionary<Guid, Product>(alreadyLoaded);
        var missing = kit.Components.Select(c => c.ProductId).Distinct().Where(id => !products.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            foreach (var product in await db.Products.Where(p => p.CompanyId == currentUser.CompanyId && missing.Contains(p.Id)).ToListAsync(ct))
            {
                products[product.Id] = product;
            }
        }
        return products;
    }

    private void RecordHistory(Kit kit, decimal previousCost, decimal previousMargin, string source)
    {
        if (kit.CostPrice == previousCost && kit.MarginPercent == previousMargin) return;

        db.KitCostHistories.Add(new KitCostHistory
        {
            CompanyId = currentUser.CompanyId,
            KitId = kit.Id,
            KitName = kit.Name,
            PreviousCost = previousCost,
            NewCost = kit.CostPrice,
            PreviousMarginPercent = previousMargin,
            NewMarginPercent = kit.MarginPercent,
            SuggestedPrice = kit.SuggestedPrice,
            Source = source,
            CreatedBy = currentUser.UserName,
        });
    }

    /// <summary>Componente montado com os dados do produto (nome, código, unidade, custo e saldo); só a quantidade vem da tela.</summary>
    private static KitComponent NewComponent(KitComponentDto c, Dictionary<Guid, Product> products)
    {
        var product = products[c.ProductId];
        return new KitComponent
        {
            ProductId = c.ProductId,
            Name = product.Name,
            Code = product.Code,
            LotNumber = c.LotNumber,
            Quantity = c.Quantity,
            Unit = product.Unit,
            UnitCost = product.CostPrice,
            Subtotal = Math.Round(c.Quantity * product.CostPrice, 2, MidpointRounding.AwayFromZero),
            AvailableStock = product.Stock,
        };
    }

    private static KitDto ToDto(Kit k, decimal companyMinMargin)
    {
        var minMargin = k.MinMarginPercent ?? companyMinMargin;
        var priceForMin = k.CostPrice > 0 ? KitPricing.PriceForMargin(k.CostPrice, minMargin) : 0m;
        var below = k.CostPrice > 0 && k.MarginPercent < minMargin;

        return new KitDto(
            k.Id, k.Code, k.Name, k.Tag, k.ServeLabel, k.Description, k.SuggestedPrice, k.CostPrice, k.MarginPercent,
            k.TotalWeightKg, k.ImageUrl, k.ComponentsSummary,
            k.Components.Select(c => new KitComponentDto(
                c.ProductId, c.Name, c.Code, c.LotNumber, c.Quantity, c.Unit, c.UnitCost, c.Subtotal, c.AvailableStock)).ToList(),
            k.StockAvailable, k.StockStatus, k.BarcodeEan,
            minMargin, k.MinMarginPercent is null, below, priceForMin, below ? Math.Max(0m, priceForMin - k.SuggestedPrice) : 0m);
    }
}
