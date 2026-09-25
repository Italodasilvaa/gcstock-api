using Girus.Application.Audit;
using Girus.Application.Abstractions;
using Girus.Application.Common;
using Girus.Application.Purchasing;
using Girus.Domain.Entities;
using Girus.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace Girus.Infra.Services;

public class PurchaseService(GirusDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit) : IPurchaseService
{
    public async Task<List<PurchaseEntryDto>> GetAllAsync(CancellationToken ct)
    {
        var purchases = await db.PurchaseEntries
            .Include(p => p.Items)
            .Where(p => p.CompanyId == currentUser.CompanyId)
            .OrderByDescending(p => p.EntryDate)
            .ToListAsync(ct);
        return purchases.Select(ToDto).ToList();
    }

    public async Task<PurchaseEntryDto> SaveAsync(SavePurchaseRequest request, CancellationToken ct)
    {
        var count = await db.PurchaseEntries.CountAsync(p => p.CompanyId == currentUser.CompanyId, ct);
        var now = DateTime.UtcNow;
        var rnd = Random.Shared;
        var items = request.Items ?? [];

        var saved = new PurchaseEntry
        {
            CompanyId = currentUser.CompanyId,
            OrderCode = "PED-2025-0" + (90 + count),
            NfeNumber = request.NfeNumber ?? "NF-e 000." + rnd.Next(10000, 100000),
            NfeSeries = request.NfeSeries ?? "Série 1",
            EntryDate = request.EntryDate ?? DateOnly.FromDateTime(now),
            EntryTime = now.ToString("HH:mm"),
            SupplierName = request.SupplierName ?? "Minerva Foods S.A.",
            SupplierCnpj = request.SupplierCnpj ?? "67.620.377/0001-47",
            SupplierCity = request.SupplierCity ?? "Barretos / SP",
            SupplierSif = request.SupplierSif ?? "SIF 421",
            ItemsCount = request.ItemsCount ?? items.Count,
            TotalWeightKg = request.TotalWeightKg ?? 160,
            TotalAmount = request.TotalAmount ?? (items.Count > 0 ? items.Sum(i => i.Subtotal) : 5765.0m),
            ShippingCost = request.ShippingCost ?? 120.0m,
            PaymentCondition = request.PaymentCondition ?? "Boleto 30 dias",
            PaymentStatus = "pendente",
            PaymentMode = request.PaymentMode ?? "a_prazo",
            Items = items.Select(i => new PurchaseItem
            {
                ProductId = i.ProductId,
                Name = i.Name,
                Code = i.Code,
                Quantity = i.Quantity,
                Unit = i.Unit,
                UnitCost = i.UnitCost,
                Subtotal = i.Subtotal,
                ExpiryDate = i.ExpiryDate,
                LotNumber = i.LotNumber,
                IsPerishable = i.IsPerishable,
            }).ToList(),
        };
        db.PurchaseEntries.Add(saved);

        // Também cria automaticamente um título em Contas a Pagar (Módulo Compras).
        var dueDate = DateOnly.FromDateTime(now.AddDays(30));
        var payable = new AccountsPayable
        {
            PurchaseEntryId = saved.Id,
            CompanyId = currentUser.CompanyId,
            Supplier = saved.SupplierName,
            Description = $"Entrada NF-e {saved.NfeNumber} ({saved.ItemsCount} itens)",
            NfeReference = saved.NfeNumber,
            IssueDate = saved.EntryDate,
            DueDate = dueDate,
            DueLabel = DateLabelHelper.DueLabelFor(dueDate),
            Amount = saved.TotalAmount,
            PaymentMethod = saved.PaymentCondition,
            Origin = "Módulo Compras",
            Status = "a_vencer",
            Category = "compras",
        };
        db.AccountsPayables.Add(payable);

        // Pix / Dinheiro / Cartão são pagos no ato: a compra e o título já nascem quitados.
        if (IsPaidOnEntry(saved.PaymentMode)) MarkAsPaid(saved, payable);

        await ApplyItemsToStockAsync(saved, items, ct);

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("criou", "Compra / Entrada", $"{saved.OrderCode} • {saved.SupplierName}", null, ct);
        return ToDto(saved);
    }

    public async Task<PurchaseEntryDto?> UpdateAsync(Guid id, SavePurchaseRequest request, CancellationToken ct)
    {
        var existing = await db.PurchaseEntries
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == currentUser.CompanyId, ct);
        if (existing is null) return null;

        // Compra paga é definitiva: editar mudaria estoque e valores de um título já quitado.
        if (existing.PaymentStatus == "pago")
            throw new InvalidOperationException("Esta compra já está paga e não pode mais ser alterada.");

        var newItems = request.Items ?? [];

        // Produtos afetados tanto pelos itens antigos (a reverter) quanto pelos novos (a aplicar) — a
        // validade "mais próxima" de todos eles precisa ser recalculada no final, mesmo os que saíram da lista.
        var touchedProductIds = existing.Items
            .Where(i => i.ProductId.HasValue)
            .Select(i => i.ProductId!.Value)
            .Concat(newItems.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value))
            .Distinct()
            .ToList();
        var products = touchedProductIds.Count > 0
            ? await db.Products.Where(p => p.CompanyId == currentUser.CompanyId && touchedProductIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct)
            : [];

        // Reverte o efeito da versão antiga: desfaz a soma de estoque e remove lote/movimentação desta compra.
        foreach (var oldItem in existing.Items)
        {
            if (oldItem.ProductId.HasValue && products.TryGetValue(oldItem.ProductId.Value, out var product))
            {
                product.Stock -= oldItem.Quantity;
            }
        }
        var oldBatches = await db.StockBatches.Where(b => b.PurchaseEntryId == existing.Id).ToListAsync(ct);
        db.StockBatches.RemoveRange(oldBatches);
        var oldMovements = await db.StockMovements.Where(m => m.PurchaseEntryId == existing.Id).ToListAsync(ct);
        db.StockMovements.RemoveRange(oldMovements);
        db.PurchaseItems.RemoveRange(existing.Items);

        // Sem isso, o recálculo de validade em ApplyItemsToStockAsync consultaria o banco e ainda
        // encontraria os lotes antigos (a exclusão só é persistida no SaveChanges final), escolhendo
        // erroneamente uma validade que já não existe mais.
        await db.SaveChangesAsync(ct);

        if (request.NfeNumber is not null) existing.NfeNumber = request.NfeNumber;
        if (request.NfeSeries is not null) existing.NfeSeries = request.NfeSeries;
        if (request.EntryDate is not null) existing.EntryDate = request.EntryDate.Value;
        if (request.SupplierName is not null) existing.SupplierName = request.SupplierName;
        if (request.SupplierCnpj is not null) existing.SupplierCnpj = request.SupplierCnpj;
        if (request.SupplierCity is not null) existing.SupplierCity = request.SupplierCity;
        if (request.SupplierSif is not null) existing.SupplierSif = request.SupplierSif;
        existing.ItemsCount = request.ItemsCount ?? newItems.Count;
        existing.TotalWeightKg = request.TotalWeightKg ?? existing.TotalWeightKg;
        existing.TotalAmount = request.TotalAmount ?? (newItems.Count > 0 ? newItems.Sum(i => i.Subtotal) : existing.TotalAmount);
        if (request.ShippingCost is not null) existing.ShippingCost = request.ShippingCost.Value;
        if (request.PaymentCondition is not null) existing.PaymentCondition = request.PaymentCondition;
        if (request.PaymentMode is not null) existing.PaymentMode = request.PaymentMode;

        if (IsPaidOnEntry(existing.PaymentMode) && existing.PaymentStatus != "pago")
        {
            var payable = await db.AccountsPayables
                .FirstOrDefaultAsync(p => p.PurchaseEntryId == existing.Id && p.CompanyId == currentUser.CompanyId, ct);
            MarkAsPaid(existing, payable);
        }

        existing.Items = newItems.Select(i => new PurchaseItem
        {
            ProductId = i.ProductId,
            Name = i.Name,
            Code = i.Code,
            Quantity = i.Quantity,
            Unit = i.Unit,
            UnitCost = i.UnitCost,
            Subtotal = i.Subtotal,
            ExpiryDate = i.ExpiryDate,
            LotNumber = i.LotNumber,
            IsPerishable = i.IsPerishable,
        }).ToList();

        await ApplyItemsToStockAsync(existing, newItems, ct, products, touchedProductIds);

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("editou", "Compra / Entrada", $"{existing.OrderCode} • {existing.SupplierName}", null, ct);
        return ToDto(existing);
    }

    private static bool IsPaidOnEntry(string paymentMode) => paymentMode is "pix" or "dinheiro" or "cartao";

    private static void MarkAsPaid(PurchaseEntry purchase, AccountsPayable? payable)
    {
        purchase.PaymentStatus = "pago";
        if (payable is null || payable.Status == "pago") return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        payable.Status = "pago";
        payable.PaidDate = today;
        payable.DueLabel = $"Pago em {today:dd/MM/yyyy}";
    }

    /// <summary>
    /// Soma o estoque de cada item com produto vinculado, registra o lote + a movimentação de entrada, e recalcula
    /// a validade "mais próxima" do Produto a partir de TODOS os lotes dele (não só os desta compra) — assim um
    /// lote com validade mais longa nunca esconde um lote mais antigo que ainda está prestes a vencer.
    /// </summary>
    private async Task ApplyItemsToStockAsync(
        PurchaseEntry entry,
        List<PurchaseItemDto> items,
        CancellationToken ct,
        Dictionary<Guid, Product>? preloadedProducts = null,
        List<Guid>? extraProductIdsToRecompute = null)
    {
        var now = DateTime.UtcNow;
        var productIds = items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value).Distinct().ToList();

        var products = preloadedProducts ?? (productIds.Count > 0
            ? await db.Products.Where(p => p.CompanyId == currentUser.CompanyId && productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct)
            : []);

        var newBatchesByProduct = new Dictionary<Guid, List<StockBatch>>();

        foreach (var item in items)
        {
            if (!item.ProductId.HasValue || !products.TryGetValue(item.ProductId.Value, out var product))
            {
                continue;
            }

            // Sem validade informada (item não perecível/sem controle de lote): assume prazo longo,
            // só para manter os campos de estoque/lote consistentes, sem gerar alerta de vencimento falso.
            var expiryDate = item.ExpiryDate ?? DateOnly.FromDateTime(now).AddYears(1);
            var daysRemaining = expiryDate.DayNumber - DateOnly.FromDateTime(now).DayNumber;

            product.Stock += item.Quantity;
            product.CostPrice = item.UnitCost;
            product.Supplier = entry.SupplierName;

            var batch = new StockBatch
            {
                CompanyId = currentUser.CompanyId,
                ProductId = product.Id,
                ProductName = item.Name,
                Sku = item.Code,
                LotNumber = item.LotNumber,
                AvailableKg = item.Quantity,
                EntryDate = entry.EntryDate,
                ExpiryDate = expiryDate,
                DaysRemaining = daysRemaining,
                FiscalDoc = entry.NfeNumber,
                Supplier = entry.SupplierName,
                Status = daysRemaining < 0 ? "vencido" : daysRemaining <= 3 ? "urgente" : daysRemaining <= 15 ? "regular" : "novo",
                PurchaseEntryId = entry.Id,
            };
            db.StockBatches.Add(batch);

            if (!newBatchesByProduct.TryGetValue(product.Id, out var list))
            {
                list = [];
                newBatchesByProduct[product.Id] = list;
            }
            list.Add(batch);

            db.StockMovements.Add(new StockMovement
            {
                CompanyId = currentUser.CompanyId,
                Timestamp = now,
                Type = "entrada",
                Title = $"Entrada: {item.Name}",
                DocInfo = $"NF-e {entry.NfeNumber}",
                AmountKg = item.Quantity,
                Description = $"Compra {entry.OrderCode} • Fornecedor {entry.SupplierName}",
                BadgeLabel = "Compra",
                AuditLink = entry.OrderCode,
                PurchaseEntryId = entry.Id,
            });
        }

        var productIdsToRecompute = newBatchesByProduct.Keys.Concat(extraProductIdsToRecompute ?? []).Distinct();
        foreach (var productId in productIdsToRecompute)
        {
            if (!products.TryGetValue(productId, out var product)) continue;

            newBatchesByProduct.TryGetValue(productId, out var newBatches);
            var existingBatches = await db.StockBatches
                .Where(b => b.ProductId == productId && b.CompanyId == currentUser.CompanyId)
                .Select(b => new { b.ExpiryDate, b.LotNumber })
                .ToListAsync(ct);

            var candidates = existingBatches
                .Select(b => (b.ExpiryDate, b.LotNumber))
                .Concat((newBatches ?? []).Select(b => (b.ExpiryDate, b.LotNumber)))
                .ToList();

            if (candidates.Count == 0)
            {
                // Produto ficou sem nenhum lote (ex.: removido da compra editada e sem outras entradas).
                product.NearestExpiry = DateOnly.FromDateTime(now).AddYears(1);
                product.ExpiryDaysRemaining = 365;
                product.ExpiryStatus = "longo";
                continue;
            }

            var nearest = candidates.OrderBy(b => b.ExpiryDate).First();
            var daysRemaining = nearest.ExpiryDate.DayNumber - DateOnly.FromDateTime(now).DayNumber;
            product.NearestExpiry = nearest.ExpiryDate;
            if (!string.IsNullOrEmpty(nearest.LotNumber)) product.LotNumber = nearest.LotNumber;
            product.ExpiryDaysRemaining = daysRemaining;
            product.ExpiryStatus = daysRemaining < 0 ? "vencido" : daysRemaining <= product.AlertDays ? "critico" : daysRemaining <= 30 ? "normal" : "longo";
        }
    }

    private static PurchaseEntryDto ToDto(PurchaseEntry p) => new(
        p.Id, p.OrderCode, p.NfeNumber, p.NfeSeries, p.EntryDate, p.EntryTime, p.SupplierName, p.SupplierCnpj,
        p.SupplierCity, p.SupplierSif, p.ItemsCount, p.TotalWeightKg, p.TotalAmount, p.ShippingCost,
        p.PaymentCondition, p.PaymentStatus, p.PaymentMode,
        p.Items.Select(i => new PurchaseItemDto(
            i.Name, i.Code, i.Quantity, i.Unit, i.UnitCost, i.Subtotal, i.ExpiryDate, i.LotNumber, i.IsPerishable, i.Id, i.ProductId)).ToList());
}
