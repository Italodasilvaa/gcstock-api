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
            .Include(p => p.Refunds)
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
        await EnsureProductsBelongToCompanyAsync(items, ct);

        // Tudo-ou-nada. O lock por empresa serializa lançamentos simultâneos: sem ele, duas compras ao mesmo tempo
        // recebiam o mesmo código PED- e passavam pela checagem de nota duplicada. O lock dos produtos impede que a
        // soma de estoque se perca se uma venda do mesmo produto acontecer junto.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockCompanyPurchasesAsync(ct);
        await EnsureInvoiceNotDuplicatedAsync(
            request.NfeNumber ?? string.Empty, request.NfeSeries ?? string.Empty, request.SupplierName!, request.SupplierCnpj ?? string.Empty, null, ct);
        await LockProductsAsync(ProductIdsOf(items), ct);

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
            Items = items.Select(NewItem).ToList(),
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

        var saveProductIds = ProductIdsOf(items);
        var saveProducts = saveProductIds.Count > 0
            ? await db.Products.Where(p => p.CompanyId == currentUser.CompanyId && saveProductIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct)
            : [];
        var costsBefore = CostHistory.Snapshot(saveProducts.Values);
        await ApplyItemsToStockAsync(saved, items, ct, saveProducts);
        await KitCostRecalculator.RecalculateForProductsAsync(
            db, currentUser.CompanyId, currentUser.UserName,
            CostHistory.RecordChanges(db, currentUser.CompanyId, currentUser.UserName, costsBefore, saveProducts.Values, "compra", saved), ct);

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("criou", "Compra / Entrada", $"{saved.OrderCode} • {saved.SupplierName}", null, ct);
        await transaction.CommitAsync(ct);
        return ToDto(saved);
    }

    public async Task<PurchaseEntryDto?> UpdateAsync(Guid id, SavePurchaseRequest request, CancellationToken ct)
    {
        // A edição pode desfazer e refazer estoque e lotes em vários SaveChanges: a transação garante tudo-ou-nada e
        // o FOR UPDATE serializa duas edições simultâneas do mesmo pedido.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockPurchaseRowAsync(id, ct);

        var existing = await db.PurchaseEntries
            .Include(p => p.Items)
            .Include(p => p.Refunds)
            .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == currentUser.CompanyId, ct);
        if (existing is null) return null;

        if (existing.IsDeleted)
        {
            throw new InvalidOperationException("Compra cancelada não pode ser editada. Lance uma nova compra.");
        }

        var newItems = request.Items ?? [];
        var supplierName = request.SupplierName ?? existing.SupplierName;
        var entryDate = request.EntryDate ?? existing.EntryDate;
        // Valida antes de qualquer alteração: a reversão de estoque abaixo já grava no banco.
        ValidateRequest(supplierName, entryDate, newItems);
        await EnsureProductsBelongToCompanyAsync(newItems, ct);

        var itemsChanged = !SameItems(existing.Items, newItems);
        var entryDateChanged = entryDate != existing.EntryDate;
        var termsChanged = entryDateChanged
            || (request.ShippingCost is not null && request.ShippingCost.Value != existing.ShippingCost)
            || (request.PaymentMode is not null && request.PaymentMode != existing.PaymentMode)
            || (request.PaymentCondition is not null && request.PaymentCondition.Trim() != existing.PaymentCondition);

        // Compra paga: só os dados cadastrais (fornecedor, nota) podem mudar. Itens, valores, pagamento e data de
        // entrada já viraram estoque, custo e caixa; para mudá-los, cancela-se a compra e lança-se outra.
        if (existing.PaymentStatus == "pago" && (itemsChanged || termsChanged))
        {
            throw new InvalidOperationException(
                "Compra já paga: itens, quantidades, custos, frete, pagamento e data de entrada não podem ser alterados. " +
                "Cancele a compra e lance uma nova.");
        }

        await LockCompanyPurchasesAsync(ct);
        await EnsureInvoiceNotDuplicatedAsync(
            request.NfeNumber ?? existing.NfeNumber, request.NfeSeries ?? existing.NfeSeries, supplierName,
            request.SupplierCnpj ?? existing.SupplierCnpj, existing.Id, ct);

        var changes = new List<string>();
        void Track(string label, string? before, string? after)
        {
            if (before != after) changes.Add($"{label}: {(string.IsNullOrEmpty(before) ? "-" : before)} → {(string.IsNullOrEmpty(after) ? "-" : after)}");
        }

        var oldCount = existing.ItemsCount;
        var oldTotal = existing.TotalAmount;
        var rebuildStock = itemsChanged || entryDateChanged;

        var touchedProductIds = ProductIdsOf(existing.Items)
            .Concat(ProductIdsOf(newItems))
            .Distinct()
            .ToList();
        await LockProductsAsync(touchedProductIds, ct);
        var products = touchedProductIds.Count > 0
            ? await db.Products.Where(p => p.CompanyId == currentUser.CompanyId && touchedProductIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct)
            : [];
        var costsBefore = CostHistory.Snapshot(products.Values);

        if (rebuildStock)
        {
            // Estorna a versão antiga por movimentações inversas (o histórico fica) e recusa se parte já foi vendida.
            await ReverseStockAsync(existing, existing.Items, products, "Edição da compra", ct);
            db.PurchaseItems.RemoveRange(existing.Items);
            await db.SaveChangesAsync(ct);
        }

        if (request.NfeNumber is not null) { Track("NF-e", existing.NfeNumber, request.NfeNumber.Trim()); existing.NfeNumber = request.NfeNumber.Trim(); }
        if (request.NfeSeries is not null) { Track("Série", existing.NfeSeries, request.NfeSeries.Trim()); existing.NfeSeries = request.NfeSeries.Trim(); }
        Track("Data de entrada", existing.EntryDate.ToString("dd/MM/yyyy"), entryDate.ToString("dd/MM/yyyy"));
        existing.EntryDate = entryDate;
        if (request.SupplierName is not null) { Track("Fornecedor", existing.SupplierName, request.SupplierName.Trim()); existing.SupplierName = request.SupplierName.Trim(); }
        if (request.SupplierCnpj is not null) { Track("CNPJ", existing.SupplierCnpj, request.SupplierCnpj.Trim()); existing.SupplierCnpj = request.SupplierCnpj.Trim(); }
        if (request.SupplierCity is not null) existing.SupplierCity = request.SupplierCity.Trim();
        if (request.SupplierSif is not null) existing.SupplierSif = request.SupplierSif.Trim();
        if (request.ShippingCost is not null)
        {
            Track("Frete", existing.ShippingCost.ToString("0.00"), request.ShippingCost.Value.ToString("0.00"));
            existing.ShippingCost = request.ShippingCost.Value;
        }
        if (request.PaymentCondition is not null) { Track("Condição de pagamento", existing.PaymentCondition, request.PaymentCondition.Trim()); existing.PaymentCondition = request.PaymentCondition.Trim(); }
        if (request.PaymentMode is not null) { Track("Forma de pagamento", existing.PaymentMode, request.PaymentMode); existing.PaymentMode = request.PaymentMode; }

        if (rebuildStock)
        {
            // Totais sempre derivados dos itens, para não divergirem do que foi somado ao estoque.
            existing.ItemsCount = newItems.Count;
            existing.TotalWeightKg = TotalWeightKg(newItems);
            existing.TotalAmount = newItems.Sum(i => i.Subtotal);
            changes.Add($"Itens: {oldCount} item(ns) {oldTotal:C} → {existing.ItemsCount} item(ns) {existing.TotalAmount:C}");

            var rebuiltItems = newItems.Select(NewItem).ToList();
            existing.Items = rebuiltItems;
            // O Id do item é gerado no construtor; sem o Add explícito o EF o trata como já existente e tenta UPDATE
            // (DbUpdateConcurrencyException: 0 linhas afetadas). Add marca como inserção.
            db.PurchaseItems.AddRange(rebuiltItems);
        }

        // A conta a pagar acompanha a compra editada (valor, fornecedor, NF-e, emissão e vencimento).
        var payable = await db.AccountsPayables
            .FirstOrDefaultAsync(p => p.PurchaseEntryId == existing.Id && p.CompanyId == currentUser.CompanyId, ct);
        if (payable is not null) SyncPayable(existing, payable);

        if (IsPaidOnEntry(existing.PaymentMode) && existing.PaymentStatus != "pago")
        {
            MarkAsPaid(existing, payable);
        }

        if (rebuildStock)
        {
            await ApplyItemsToStockAsync(existing, newItems, ct, products, touchedProductIds);
            await KitCostRecalculator.RecalculateForProductsAsync(
                db, currentUser.CompanyId, currentUser.UserName,
                CostHistory.RecordChanges(db, currentUser.CompanyId, currentUser.UserName, costsBefore, products.Values, "edicao_compra", existing), ct);
        }
        else
        {
            // Sem mexer em estoque: os lotes só acompanham os dados da nota e do fornecedor.
            var batches = await ActiveBatchesAsync(existing.Id, ct);
            foreach (var batch in batches)
            {
                batch.FiscalDoc = existing.NfeNumber;
                batch.Supplier = existing.SupplierName;
            }
        }

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(
            "editou", "Compra / Entrada", $"{existing.OrderCode} • {existing.SupplierName}",
            changes.Count > 0 ? string.Join("; ", changes) : null, ct);
        await transaction.CommitAsync(ct);
        return ToDto(existing);
    }

    public async Task<PurchaseEntryDto?> CancelAsync(Guid id, string? reason, CancellationToken ct)
    {
        var cleanReason = reason?.Trim();
        if (string.IsNullOrEmpty(cleanReason)) throw new ArgumentException("Informe o motivo do cancelamento.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockPurchaseRowAsync(id, ct);

        var existing = await db.PurchaseEntries
            .Include(p => p.Items)
            .Include(p => p.Refunds)
            .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == currentUser.CompanyId, ct);
        if (existing is null) return null;
        if (existing.IsDeleted) throw new InvalidOperationException("Esta compra já foi cancelada.");

        var productIds = ProductIdsOf(existing.Items);
        await LockProductsAsync(productIds, ct);
        var products = productIds.Count > 0
            ? await db.Products.Where(p => p.CompanyId == currentUser.CompanyId && productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct)
            : [];
        var costsBefore = CostHistory.Snapshot(products.Values);

        // Falha aqui (InvalidOperationException) se parte do que entrou já foi vendida ou perdida: nada é gravado.
        await ReverseStockAsync(existing, existing.Items, products, $"Cancelamento: {cleanReason}", ct);

        var wasPaid = existing.PaymentStatus == "pago";
        existing.IsDeleted = true;
        existing.CancelledAt = DateTime.UtcNow;
        existing.CancelledBy = currentUser.UserName;
        existing.CancelReason = cleanReason;

        // Título ainda em aberto não representa dinheiro que saiu: some das contas a pagar. Título já pago é
        // movimentação financeira e fica intacto; o que falta é a devolução do fornecedor (RegisterRefundAsync).
        if (!wasPaid)
        {
            var openPayables = await db.AccountsPayables
                .Where(p => p.PurchaseEntryId == existing.Id && p.CompanyId == currentUser.CompanyId && p.Status != "pago")
                .ToListAsync(ct);
            db.AccountsPayables.RemoveRange(openPayables);
        }

        await db.SaveChangesAsync(ct);
        await RefreshProductsAfterReversalAsync(products.Values, ct);
        await KitCostRecalculator.RecalculateForProductsAsync(
            db, currentUser.CompanyId, currentUser.UserName,
            CostHistory.RecordChanges(db, currentUser.CompanyId, currentUser.UserName, costsBefore, products.Values, "cancelamento_compra", existing), ct);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(
            "cancelou", "Compra / Entrada", $"{existing.OrderCode} • {existing.SupplierName}",
            $"Motivo: {cleanReason}" + (wasPaid ? " • Compra já paga: devolução do fornecedor pendente" : null), ct);
        await transaction.CommitAsync(ct);
        return ToDto(existing);
    }

    public async Task<PurchaseEntryDto?> RegisterRefundAsync(Guid id, RegisterPurchaseRefundRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockPurchaseRowAsync(id, ct);

        var existing = await db.PurchaseEntries
            .Include(p => p.Items)
            .Include(p => p.Refunds)
            .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == currentUser.CompanyId, ct);
        if (existing is null) return null;

        if (!existing.IsDeleted || existing.PaymentStatus != "pago")
        {
            throw new InvalidOperationException("Só é possível registrar devolução de uma compra paga que foi cancelada.");
        }

        var amount = Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0) throw new ArgumentException("Informe o valor devolvido pelo fornecedor.");

        var alreadyRefunded = existing.Refunds.Sum(r => r.Amount);
        if (alreadyRefunded + amount > existing.TotalAmount + 0.005m)
        {
            throw new ArgumentException(
                $"O valor passa do que foi pago: já foram devolvidos {alreadyRefunded:C} de {existing.TotalAmount:C}.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
        var date = request.Date ?? today;
        if (date > today.AddDays(1)) throw new ArgumentException("A data da devolução não pode ser no futuro.");

        var refund = new PurchaseRefund
        {
            CompanyId = currentUser.CompanyId,
            PurchaseEntryId = existing.Id,
            Amount = amount,
            Date = date,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedBy = currentUser.UserName,
        };
        db.PurchaseRefunds.Add(refund);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(
            "registrou devolução", "Compra / Entrada", $"{existing.OrderCode} • {existing.SupplierName}",
            $"Devolvido pelo fornecedor: {amount:C} em {date:dd/MM/yyyy}", ct);
        await transaction.CommitAsync(ct);
        return ToDto(existing);
    }

    private async Task LockPurchaseRowAsync(Guid id, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"PurchaseEntries\" WHERE \"Id\" = {id} AND \"CompanyId\" = {currentUser.CompanyId} FOR UPDATE", ct);

    /// <summary>Serializa os lançamentos de compra da empresa (código PED- e nota duplicada) até o fim da transação.</summary>
    private async Task LockCompanyPurchasesAsync(CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({currentUser.CompanyId.ToString()}, 0))", ct);

    /// <summary>Trava os produtos até o fim da transação (a venda faz o mesmo): somas e estornos de estoque não se perdem.</summary>
    private async Task LockProductsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct)
    {
        if (productIds.Count == 0) return;
        var ids = productIds.ToArray();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Products\" WHERE \"Id\" = ANY({ids}) AND \"CompanyId\" = {currentUser.CompanyId} FOR UPDATE", ct);
    }

    private static List<Guid> ProductIdsOf(IEnumerable<PurchaseItemDto> items) =>
        items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value).Distinct().ToList();

    private static List<Guid> ProductIdsOf(IEnumerable<PurchaseItem> items) =>
        items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value).Distinct().ToList();

    private static PurchaseItem NewItem(PurchaseItemDto i) => new()
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
    };

    /// <summary>Os itens enviados são os mesmos já gravados? (ordem não importa)</summary>
    private static bool SameItems(List<PurchaseItem> current, List<PurchaseItemDto> incoming)
    {
        if (current.Count != incoming.Count) return false;

        var a = current
            .Select(i => (i.ProductId, i.Name, i.Code, i.Quantity, i.Unit, i.UnitCost, i.Subtotal, i.ExpiryDate, LotNumber: i.LotNumber ?? string.Empty, i.IsPerishable))
            .OrderBy(x => x.ProductId).ThenBy(x => x.Name).ThenBy(x => x.LotNumber).ThenBy(x => x.Quantity).ThenBy(x => x.UnitCost)
            .ToList();
        var b = incoming
            .Select(i => (i.ProductId, i.Name, i.Code, i.Quantity, i.Unit, i.UnitCost, i.Subtotal, i.ExpiryDate, LotNumber: i.LotNumber ?? string.Empty, i.IsPerishable))
            .OrderBy(x => x.ProductId).ThenBy(x => x.Name).ThenBy(x => x.LotNumber).ThenBy(x => x.Quantity).ThenBy(x => x.UnitCost)
            .ToList();

        return a.Zip(b).All(pair => pair.First.Equals(pair.Second));
    }

    /// <summary>Mesma nota (número + série) do mesmo fornecedor não pode ser lançada duas vezes; compra cancelada não conta.</summary>
    private async Task EnsureInvoiceNotDuplicatedAsync(
        string nfeNumber, string nfeSeries, string supplierName, string supplierCnpj, Guid? ignoreId, CancellationToken ct)
    {
        var number = nfeNumber.Trim();
        if (number.Length == 0) return;

        var series = nfeSeries.Trim();
        var candidates = await db.PurchaseEntries
            .Where(p => p.CompanyId == currentUser.CompanyId && !p.IsDeleted
                && p.NfeNumber == number && p.NfeSeries == series && (ignoreId == null || p.Id != ignoreId))
            .Select(p => new { p.OrderCode, p.SupplierName, p.SupplierCnpj })
            .ToListAsync(ct);

        var cnpj = OnlyDigits(supplierCnpj);
        var name = supplierName.Trim();
        var duplicate = candidates.FirstOrDefault(c =>
        {
            var otherCnpj = OnlyDigits(c.SupplierCnpj);
            return cnpj.Length > 0 && otherCnpj.Length > 0
                ? cnpj == otherCnpj
                : string.Equals(c.SupplierName.Trim(), name, StringComparison.OrdinalIgnoreCase);
        });

        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"A NF-e {number}{(series.Length > 0 ? $" (série {series})" : "")} deste fornecedor já foi lançada na compra {duplicate.OrderCode}. " +
                "Se ela foi lançada errada, cancele-a antes de lançar de novo.");
        }

        static string OnlyDigits(string value) => new(value.Where(char.IsDigit).ToArray());
    }

    private async Task<List<StockBatch>> ActiveBatchesAsync(Guid purchaseEntryId, CancellationToken ct) =>
        await db.StockBatches
            .Where(b => b.PurchaseEntryId == purchaseEntryId && b.CompanyId == currentUser.CompanyId && b.Status != "cancelado")
            .ToListAsync(ct);

    /// <summary>
    /// Estorna o que a compra somou ao estoque sem apagar histórico: tira do estoque do produto e do custo médio, zera os
    /// lotes (a linha continua, marcada "cancelado") e grava uma movimentação inversa por item. Recusa se algo já saiu.
    /// </summary>
    private async Task ReverseStockAsync(
        PurchaseEntry entry, List<PurchaseItem> items, Dictionary<Guid, Product> products, string reason, CancellationToken ct)
    {
        var batches = await ActiveBatchesAsync(entry.Id, ct);

        // Nada do que entrou pode ter saído (venda, perda, ajuste manual): o estorno deixaria o estoque negativo.
        var received = items
            .Where(i => i.ProductId.HasValue && products.ContainsKey(i.ProductId.Value))
            .GroupBy(i => i.ProductId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        foreach (var (productId, quantity) in received)
        {
            var availableInLots = batches.Where(b => b.ProductId == productId).Sum(b => b.AvailableKg);
            if (availableInLots + 0.0005m < quantity || products[productId].Stock + 0.0005m < quantity)
            {
                throw new InvalidOperationException(
                    $"Parte do pedido {entry.OrderCode} já foi vendida, perdida ou ajustada. " +
                    "Cancele as vendas/perdas dele antes de alterar ou cancelar a compra.");
            }
        }

        var now = DateTime.UtcNow;
        foreach (var item in items)
        {
            if (!item.ProductId.HasValue || !products.TryGetValue(item.ProductId.Value, out var product)) continue;

            RemoveFromAverageCost(product, item.Quantity, item.UnitCost);
            product.Stock = Math.Max(0, product.Stock - item.Quantity);

            db.StockMovements.Add(new StockMovement
            {
                CompanyId = currentUser.CompanyId,
                Timestamp = now,
                Type = "estorno_compra",
                Title = $"Estorno: {item.Name}",
                DocInfo = $"NF-e {entry.NfeNumber}",
                AmountKg = -item.Quantity,
                Unit = product.Unit,
                Description = $"{reason} • Compra {entry.OrderCode} • Fornecedor {entry.SupplierName}",
                BadgeLabel = "Estorno",
                AuditLink = entry.OrderCode,
                PurchaseEntryId = entry.Id,
            });
        }

        foreach (var batch in batches)
        {
            batch.AvailableKg = 0;
            batch.Status = "cancelado";
        }
    }

    /// <summary>Depois de um estorno gravado: validade mais próxima e, se os lotes que sobraram cobrem o estoque, custo médio deles.</summary>
    private async Task RefreshProductsAfterReversalAsync(IEnumerable<Product> products, CancellationToken ct)
    {
        var now = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
        foreach (var product in products)
        {
            var nearest = await db.StockBatches
                .Where(b => b.ProductId == product.Id && b.CompanyId == currentUser.CompanyId && b.AvailableKg > 0)
                .OrderBy(b => b.ExpiryDate)
                .Select(b => (DateOnly?)b.ExpiryDate)
                .FirstOrDefaultAsync(ct);
            var expiry = nearest ?? now.AddYears(1);
            var days = expiry.DayNumber - now.DayNumber;
            product.NearestExpiry = expiry;
            product.ExpiryDaysRemaining = days;
            product.ExpiryStatus = days < 0 ? "vencido" : days <= product.AlertDays ? "critico" : days <= 30 ? "normal" : "longo";

            // Se os lotes que sobraram cobrem todo o estoque e têm custo, o custo médio passa a ser o deles (o que sobra de verdade).
            var remaining = await db.StockBatches
                .Where(b => b.ProductId == product.Id && b.CompanyId == currentUser.CompanyId && b.AvailableKg > 0 && b.UnitCost > 0)
                .Select(b => new { b.AvailableKg, b.UnitCost })
                .ToListAsync(ct);
            var remainingQty = remaining.Sum(b => b.AvailableKg);
            if (remainingQty > 0 && Math.Abs(remainingQty - product.Stock) < 0.001m)
            {
                product.CostPrice = CostHistory.Round(remaining.Sum(b => b.AvailableKg * b.UnitCost) / remainingQty);
            }
        }
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
        payable.Status = dueDate < DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3)) ? "vencido" : "a_vencer";
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
    /// <summary>Itens vinculados a produto só podem apontar para produtos da empresa (antes um ID alheio era ignorado em silêncio).</summary>
    private async Task EnsureProductsBelongToCompanyAsync(List<PurchaseItemDto> items, CancellationToken ct)
    {
        var productIds = items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value).Distinct().ToList();
        if (productIds.Count == 0) return;

        var found = await db.Products.CountAsync(p => p.CompanyId == currentUser.CompanyId && productIds.Contains(p.Id), ct);
        if (found != productIds.Count)
        {
            throw new ArgumentException("Um dos produtos da compra não foi encontrado.");
        }
    }

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
    /// <summary>
    /// Custo médio móvel do estoque: a entrada se mistura ao que já está no estoque,
    /// novo custo = (estoque atual × custo atual + quantidade × custo da compra) ÷ (estoque atual + quantidade).
    /// A venda não altera o custo; ela sai pelo custo médio do momento (ver PosService).
    /// </summary>
    private static void AddToAverageCost(Product product, decimal quantity, decimal unitCost)
    {
        var stockBefore = Math.Max(0, product.Stock);
        var total = stockBefore + quantity;
        product.CostPrice = total > 0
            ? CostHistory.Round((stockBefore * product.CostPrice + quantity * unitCost) / total)
            : unitCost;
    }

    /// <summary>
    /// Desfaz a contribuição de um item de compra (usado ao editar/trocar os itens de um pedido): tira a quantidade
    /// e o valor daquele item da média do estoque atual. Se não sobra estoque, o custo atual fica até a próxima entrada.
    /// </summary>
    private static void RemoveFromAverageCost(Product product, decimal quantity, decimal unitCost)
    {
        var remaining = product.Stock - quantity;
        if (product.Stock <= 0 || remaining <= 0) return;

        var value = product.Stock * product.CostPrice - quantity * unitCost;
        product.CostPrice = CostHistory.Round(Math.Max(0, value / remaining));
    }

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

            AddToAverageCost(product, item.Quantity, item.UnitCost);
            product.Stock += item.Quantity;
            product.Supplier = entry.SupplierName;

            var batch = new StockBatch
            {
                CompanyId = currentUser.CompanyId,
                ProductId = product.Id,
                ProductName = item.Name,
                Sku = item.Code,
                LotNumber = item.LotNumber,
                AvailableKg = item.Quantity,
                UnitCost = item.UnitCost,
                Unit = product.Unit,
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
                Unit = product.Unit,
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
                .Where(b => b.ProductId == productId && b.CompanyId == currentUser.CompanyId && b.Status != "cancelado")
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

        // Lotes não podem somar mais que o estoque do produto (ex.: compra editada depois de vendas já feitas).
        await StockLots.ReconcileAsync(db, currentUser.CompanyId, [.. productIdsToRecompute.Where(products.ContainsKey).Select(id => products[id])], ct);
    }

    private static PurchaseEntryDto ToDto(PurchaseEntry p)
    {
        var refunded = p.Refunds.Sum(r => r.Amount);
        // Devolução só existe para compra paga e cancelada; enquanto não houver devolução registrada, fica pendente.
        var refundStatus = p.IsDeleted && p.PaymentStatus == "pago"
            ? refunded <= 0 ? "pendente" : refunded + 0.005m < p.TotalAmount ? "parcial" : "devolvido"
            : "nao_aplicavel";

        return new PurchaseEntryDto(
            p.Id, p.OrderCode, p.NfeNumber, p.NfeSeries, p.EntryDate, p.EntryTime, p.SupplierName, p.SupplierCnpj,
            p.SupplierCity, p.SupplierSif, p.ItemsCount, p.TotalWeightKg, p.TotalAmount, p.ShippingCost,
            p.PaymentCondition, p.PaymentStatus, p.PaymentMode,
            p.Items.Select(i => new PurchaseItemDto(
                i.Name, i.Code, i.Quantity, i.Unit, i.UnitCost, i.Subtotal, i.ExpiryDate, i.LotNumber, i.IsPerishable, i.Id, i.ProductId)).ToList(),
            p.IsDeleted, p.CancelledAt, p.CancelledBy, p.CancelReason, refundStatus, refunded,
            p.Refunds.OrderBy(r => r.Date).ThenBy(r => r.CreatedAt)
                .Select(r => new PurchaseRefundDto(r.Id, r.Amount, r.Date, r.Notes, r.CreatedBy, r.CreatedAt)).ToList());
    }
}
