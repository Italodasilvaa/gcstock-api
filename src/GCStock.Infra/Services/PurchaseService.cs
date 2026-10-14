using System.Text.RegularExpressions;
using GCStock.Application.Audit;
using GCStock.Application.Abstractions;
using GCStock.Application.Common;
using GCStock.Application.Purchasing;
using GCStock.Domain.Entities;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

public class PurchaseService(GCStockDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit) : IPurchaseService
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
        var now = DateTime.UtcNow;
        var items = request.Items ?? [];
        var entryDate = request.EntryDate ?? DateOnly.FromDateTime(now);
        ValidateRequest(request.SupplierName, entryDate, items);

        var saved = new PurchaseEntry
        {
            CompanyId = currentUser.CompanyId,
            OrderCode = await NextOrderCodeAsync(entryDate.Year, ct),
            NfeNumber = request.NfeNumber?.Trim() ?? string.Empty,
            NfeSeries = request.NfeSeries?.Trim() ?? string.Empty,
            EntryDate = entryDate,
            EntryTime = now.ToString("HH:mm"),
            SupplierName = request.SupplierName!.Trim(),
            SupplierCnpj = request.SupplierCnpj?.Trim() ?? string.Empty,
            SupplierCity = request.SupplierCity?.Trim() ?? string.Empty,
            SupplierSif = request.SupplierSif?.Trim() ?? string.Empty,
            ItemsCount = items.Count,
            TotalWeightKg = TotalWeightKg(items),
            TotalAmount = items.Sum(i => i.Subtotal),
            ShippingCost = request.ShippingCost ?? 0,
            PaymentCondition = request.PaymentCondition?.Trim() ?? string.Empty,
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
        var payable = new AccountsPayable
        {
            PurchaseEntryId = saved.Id,
            CompanyId = currentUser.CompanyId,
            Origin = "Módulo Compras",
            Status = "a_vencer",
            Category = "compras",
        };
        SyncPayable(saved, payable);
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
        // Valida antes de qualquer alteração: o SaveChanges intermediário abaixo já desfaz o estoque antigo.
        ValidateRequest(request.SupplierName ?? existing.SupplierName, request.EntryDate ?? existing.EntryDate, newItems);

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

        if (request.NfeNumber is not null) existing.NfeNumber = request.NfeNumber.Trim();
        if (request.NfeSeries is not null) existing.NfeSeries = request.NfeSeries.Trim();
        if (request.EntryDate is not null) existing.EntryDate = request.EntryDate.Value;
        if (request.SupplierName is not null) existing.SupplierName = request.SupplierName.Trim();
        if (request.SupplierCnpj is not null) existing.SupplierCnpj = request.SupplierCnpj.Trim();
        if (request.SupplierCity is not null) existing.SupplierCity = request.SupplierCity.Trim();
        if (request.SupplierSif is not null) existing.SupplierSif = request.SupplierSif.Trim();
        // Totais sempre derivados dos itens, para não divergirem do que foi somado ao estoque.
        existing.ItemsCount = newItems.Count;
        existing.TotalWeightKg = TotalWeightKg(newItems);
        existing.TotalAmount = newItems.Sum(i => i.Subtotal);
        if (request.ShippingCost is not null) existing.ShippingCost = request.ShippingCost.Value;
        if (request.PaymentCondition is not null) existing.PaymentCondition = request.PaymentCondition.Trim();
        if (request.PaymentMode is not null) existing.PaymentMode = request.PaymentMode;

        // A conta a pagar acompanha a compra editada (valor, fornecedor, NF-e, emissão e vencimento).
        var payable = await db.AccountsPayables
            .FirstOrDefaultAsync(p => p.PurchaseEntryId == existing.Id && p.CompanyId == currentUser.CompanyId, ct);
        if (payable is not null) SyncPayable(existing, payable);

        if (IsPaidOnEntry(existing.PaymentMode) && existing.PaymentStatus != "pago")
        {
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

    /// <summary>Pix/Dinheiro/Cartão: pago no dia da entrada da mercadoria.</summary>
    private static void MarkAsPaid(PurchaseEntry purchase, AccountsPayable? payable)
    {
        purchase.PaymentStatus = "pago";
        if (payable is null || payable.Status == "pago") return;

        payable.Status = "pago";
        payable.PaidDate = purchase.EntryDate;
        payable.DueDate = purchase.EntryDate;
        payable.DueLabel = $"Pago em {purchase.EntryDate:dd/MM/yyyy}";
    }

    /// <summary>Copia para a conta a pagar os dados da compra. Vencimento = data da entrada + prazo da condição de pagamento.</summary>
    private static void SyncPayable(PurchaseEntry purchase, AccountsPayable payable)
    {
        var nfeLabel = string.IsNullOrEmpty(purchase.NfeNumber) ? purchase.OrderCode : $"NF-e {purchase.NfeNumber}";
        payable.Supplier = purchase.SupplierName;
        payable.Description = $"Entrada {nfeLabel} ({purchase.ItemsCount} {(purchase.ItemsCount == 1 ? "item" : "itens")})";
        payable.NfeReference = purchase.NfeNumber;
        payable.IssueDate = purchase.EntryDate;
        payable.Amount = purchase.TotalAmount;
        payable.PaymentMethod = purchase.PaymentCondition;

        if (payable.Status == "pago") return;
        var dueDate = purchase.EntryDate.AddDays(PaymentTermDays(purchase.PaymentCondition));
        payable.DueDate = dueDate;
        payable.DueLabel = DateLabelHelper.DueLabelFor(dueDate);
        payable.Status = dueDate < DateOnly.FromDateTime(DateTime.UtcNow) ? "vencido" : "a_vencer";
    }

    /// <summary>Prazo em dias a partir da condição digitada: "à vista" = 0; "Boleto 28 dias" = 28; "30/60/90" = 30 (1ª parcela); sem número = 30.</summary>
    internal static int PaymentTermDays(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) return DefaultPaymentTermDays;
        var normalized = condition.ToLowerInvariant().Replace("à", "a");
        if (normalized.Contains("a vista") || normalized.Contains("avista")) return 0;

        var match = Regex.Match(normalized, @"\d+");
        return match.Success && int.TryParse(match.Value, out var days) && days <= 365 ? days : DefaultPaymentTermDays;
    }

    private const int DefaultPaymentTermDays = 30;

    private static decimal TotalWeightKg(List<PurchaseItemDto> items) =>
        items.Where(i => string.Equals(i.Unit, "kg", StringComparison.OrdinalIgnoreCase)).Sum(i => i.Quantity);

    /// <summary>Regras de negócio da compra; ArgumentException vira 400 no endpoint.</summary>
    private static void ValidateRequest(string? supplierName, DateOnly entryDate, List<PurchaseItemDto> items)
    {
        if (string.IsNullOrWhiteSpace(supplierName))
            throw new ArgumentException("Informe o nome do fornecedor.");
        if (entryDate > DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)) // +1 dia de folga para fuso horário
            throw new ArgumentException("A data da entrada não pode ser no futuro.");
        if (items.Count == 0)
            throw new ArgumentException("Adicione ao menos um item à compra.");
        if (items.Any(i => i.Quantity <= 0))
            throw new ArgumentException("Todos os itens precisam de quantidade maior que zero.");
        if (items.Any(i => i.UnitCost < 0))
            throw new ArgumentException("O custo unitário não pode ser negativo.");
    }

    /// <summary>PED-{ano}-{sequência}, único por empresa (pula códigos já usados).</summary>
    private async Task<string> NextOrderCodeAsync(int year, CancellationToken ct)
    {
        var prefix = $"PED-{year}-";
        var existingCodes = await db.PurchaseEntries
            .Where(p => p.CompanyId == currentUser.CompanyId && p.OrderCode.StartsWith(prefix))
            .Select(p => p.OrderCode)
            .ToListAsync(ct);

        var next = existingCodes
            .Select(c => int.TryParse(c[prefix.Length..], out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        return $"{prefix}{next:D4}";
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
                // "urgente" segue os dias de alerta do produto (mesma régua do ExpiryStatus "critico" do Produto).
                Status = daysRemaining < 0 ? "vencido" : daysRemaining <= product.AlertDays ? "urgente" : daysRemaining <= 15 ? "regular" : "novo",
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
