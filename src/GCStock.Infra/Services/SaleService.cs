using System.Globalization;
using GCStock.Application.Abstractions;
using GCStock.Application.Audit;
using GCStock.Application.Sales;
using GCStock.Domain.Entities;
using GCStock.Domain.Services;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

public class SaleService(GCStockDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit) : ISaleService
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public async Task<List<SaleHistoryDto>> GetAllAsync(CancellationToken ct)
    {
        var sales = await db.SaleRecords
            .Include(s => s.Items)
            .Where(s => s.CompanyId == currentUser.CompanyId)
            .OrderByDescending(s => s.Timestamp)
            .Take(1000)
            .ToListAsync(ct);

        var saleIds = sales.Select(s => s.Id).ToList();
        var returns = await db.SaleReturns
            .Include(r => r.Items)
            .Where(r => r.CompanyId == currentUser.CompanyId && saleIds.Contains(r.SaleRecordId))
            .ToListAsync(ct);
        var returnsBySale = returns.GroupBy(r => r.SaleRecordId).ToDictionary(g => g.Key, g => g.ToList());

        return sales.Select(s => ToDto(s, returnsBySale.GetValueOrDefault(s.Id) ?? [])).ToList();
    }

    public async Task<CancelSaleResult?> CancelAsync(Guid saleId, string? reason, CancellationToken ct)
    {
        // Transação + trava da venda: dois cancelamentos simultâneos não devolvem o estoque duas vezes.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"SaleRecords\" WHERE \"SaleId\" = {saleId} AND \"CompanyId\" = {currentUser.CompanyId} FOR UPDATE", ct);

        var sale = await db.SaleRecords
            .Include(s => s.Items).ThenInclude(i => i.Lots)
            .FirstOrDefaultAsync(s => s.SaleId == saleId && s.CompanyId == currentUser.CompanyId, ct);
        if (sale is null) return null;

        if (sale.Status == "cancelada")
        {
            throw new InvalidOperationException("Esta venda já foi cancelada.");
        }

        // Cancelar depois de devolver devolveria o estoque duas vezes: com devolução registrada, devolve-se o que falta.
        if (await db.SaleReturns.AnyAsync(r => r.SaleRecordId == sale.Id && r.CompanyId == currentUser.CompanyId, ct))
        {
            throw new InvalidOperationException(
                "Esta venda já tem devolução registrada e não pode ser cancelada. Registre a devolução dos itens que faltam.");
        }

        // Os títulos a receber da venda (cartão ou a prazo, 1 por parcela) são identificados pela própria venda. Títulos
        // antigos, sem vínculo, só são achados pelo número do cupom, e apenas os que ainda não pertencem a nenhuma venda.
        var receivables = await db.AccountsReceivables
            .Where(r => r.CompanyId == currentUser.CompanyId && r.SaleId == sale.SaleId)
            .ToListAsync(ct);
        if (receivables.Count == 0 && !string.IsNullOrEmpty(sale.ReceiptNumber))
        {
            receivables = await db.AccountsReceivables
                .Where(r => r.CompanyId == currentUser.CompanyId && r.SaleId == null && r.Reference == sale.ReceiptNumber)
                .ToListAsync(ct);
        }
        if (receivables.Any(r => r.Status == "recebido"))
        {
            throw new InvalidOperationException(
                "O valor desta venda já foi recebido (título liquidado em Contas a Receber) e não pode ser cancelado por aqui.");
        }

        var now = DateTime.UtcNow;
        var notices = new List<string>();

        // Devolve o estoque: produto avulso volta ele mesmo; kit devolve cada componente e o saldo de kits.
        var kitIds = sale.Items.Where(i => i.KitId.HasValue).Select(i => i.KitId!.Value).Distinct().ToList();
        var kits = kitIds.Count > 0
            ? await db.Kits.Include(k => k.Components).Where(k => k.CompanyId == currentUser.CompanyId && kitIds.Contains(k.Id)).ToDictionaryAsync(k => k.Id, ct)
            : new Dictionary<Guid, Kit>();

        var productIds = sale.Items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value)
            .Concat(kits.Values.SelectMany(k => k.Components.Select(c => c.ProductId)))
            .Distinct().ToList();
        var products = productIds.Count > 0
            ? await db.Products.Where(p => p.CompanyId == currentUser.CompanyId && productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct)
            : new Dictionary<Guid, Product>();

        // Lotes de onde a venda tirou: a quantidade volta para o MESMO lote (se ele ainda existir). O que não tiver lote
        // de origem (venda antiga, lote refeito por edição de compra) fica no estoque do produto como "Sem lote".
        var lotIds = sale.Items.SelectMany(i => i.Lots).Select(l => l.StockBatchId).Distinct().ToList();
        var lots = lotIds.Count > 0
            ? await db.StockBatches.Where(b => b.CompanyId == currentUser.CompanyId && lotIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct)
            : new Dictionary<Guid, StockBatch>();
        foreach (var consumption in sale.Items.SelectMany(i => i.Lots))
        {
            if (lots.TryGetValue(consumption.StockBatchId, out var lot)) lot.AvailableKg += consumption.Qty;
        }

        foreach (var item in sale.Items)
        {
            var restored = false;

            if (item.ProductId.HasValue && products.TryGetValue(item.ProductId.Value, out var product))
            {
                product.Stock += item.Qty;
                restored = true;
            }
            else if (item.KitId.HasValue && kits.TryGetValue(item.KitId.Value, out var kit))
            {
                kit.StockAvailable += item.Qty;
                foreach (var component in kit.Components)
                {
                    if (products.TryGetValue(component.ProductId, out var componentProduct))
                    {
                        componentProduct.Stock += component.Quantity * item.Qty;
                    }
                }
                restored = true;
            }

            if (!restored)
            {
                notices.Add(item.Name);
                continue;
            }

            db.StockMovements.Add(new StockMovement
            {
                CompanyId = currentUser.CompanyId,
                Timestamp = now,
                Type = "estorno_venda",
                Title = $"Venda cancelada {sale.ReceiptNumber}: {item.Name}",
                DocInfo = $"Estorno da venda {sale.ReceiptNumber}",
                AmountKg = item.Qty,
                Unit = item.Unit,
                Description = $"Estoque devolvido pelo cancelamento da venda {sale.ReceiptNumber}",
                BadgeLabel = "Venda cancelada",
                AuditLink = sale.ReceiptNumber,
            });
        }

        // Desfaz o financeiro da venda.
        db.AccountsReceivables.RemoveRange(receivables);

        sale.Status = "cancelada";
        sale.CanceledAt = now;
        sale.CanceledBy = currentUser.UserName;
        sale.CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(
            "cancelou", "Venda PDV",
            $"{sale.ReceiptNumber} • {sale.Total.ToString("C", PtBr)}",
            sale.CancelReason, ct);
        await transaction.CommitAsync(ct);

        var notice = notices.Count > 0
            ? $"Estoque não devolvido (item sem vínculo com o cadastro): {string.Join(", ", notices)}. Ajuste manualmente se necessário."
            : null;
        return new CancelSaleResult(ToDto(sale, []), notice);
    }

    public async Task<SaleHistoryDto?> ReturnItemsAsync(Guid saleId, ReturnSaleRequest request, CancellationToken ct)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason)) throw new ArgumentException("Informe o motivo da devolução.");

        var lines = request.Lines ?? [];
        if (lines.Count == 0 || lines.Any(l => l.Qty <= 0))
        {
            throw new ArgumentException("Informe os itens devolvidos, com quantidade maior que zero.");
        }
        if (lines.GroupBy(l => l.SaleItemId).Any(g => g.Count() > 1))
        {
            throw new ArgumentException("Cada item da venda só pode aparecer uma vez na devolução.");
        }

        // Transação + trava da venda: duas devoluções simultâneas não passam da quantidade vendida nem devolvem estoque em dobro.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"SaleRecords\" WHERE \"SaleId\" = {saleId} AND \"CompanyId\" = {currentUser.CompanyId} FOR UPDATE", ct);

        var sale = await db.SaleRecords
            .Include(s => s.Items).ThenInclude(i => i.Lots)
            .FirstOrDefaultAsync(s => s.SaleId == saleId && s.CompanyId == currentUser.CompanyId, ct);
        if (sale is null) return null;
        if (sale.Status == "cancelada") throw new InvalidOperationException("Venda cancelada não aceita devolução.");

        var itemIds = sale.Items.Select(i => i.Id).ToList();
        var previousLines = await db.SaleReturnItems.Where(r => itemIds.Contains(r.SaleItemId)).ToListAsync(ct);
        var returnedQty = previousLines.GroupBy(p => p.SaleItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        var restockedQty = previousLines.Where(p => p.Restocked).GroupBy(p => p.SaleItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        var previousRefund = await db.SaleReturns
            .Where(r => r.SaleRecordId == sale.Id && r.CompanyId == currentUser.CompanyId)
            .SumAsync(r => (decimal?)r.RefundAmount, ct) ?? 0m;

        var kitIds = sale.Items.Where(i => i.KitId.HasValue).Select(i => i.KitId!.Value).Distinct().ToList();
        var kits = kitIds.Count > 0
            ? await db.Kits.Include(k => k.Components).Where(k => k.CompanyId == currentUser.CompanyId && kitIds.Contains(k.Id)).ToDictionaryAsync(k => k.Id, ct)
            : new Dictionary<Guid, Kit>();
        var productIds = sale.Items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value)
            .Concat(kits.Values.SelectMany(k => k.Components.Select(c => c.ProductId)))
            .Distinct().ToList();
        if (productIds.Count > 0)
        {
            var ids = productIds.ToArray();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Products\" WHERE \"Id\" = ANY({ids}) AND \"CompanyId\" = {currentUser.CompanyId} FOR UPDATE", ct);
        }
        var products = productIds.Count > 0
            ? await db.Products.Where(p => p.CompanyId == currentUser.CompanyId && productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct)
            : new Dictionary<Guid, Product>();
        var lotIds = sale.Items.SelectMany(i => i.Lots).Select(l => l.StockBatchId).Distinct().ToList();
        var lots = lotIds.Count > 0
            ? await db.StockBatches.Where(b => b.CompanyId == currentUser.CompanyId && lotIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct)
            : new Dictionary<Guid, StockBatch>();

        // O cliente pagou o total da venda (itens - desconto + taxa repassada): a devolução volta na mesma proporção.
        var itemsSubtotal = sale.Items.Sum(i => i.Subtotal);
        var paidFactor = itemsSubtotal > 0 ? sale.Total / itemsSubtotal : 1m;
        var netFactor = itemsSubtotal > 0 ? sale.NetAmount / itemsSubtotal : 1m;

        var now = DateTime.UtcNow;
        var today = ExpiryRules.Today(now);
        var saleReturn = new SaleReturn
        {
            CompanyId = currentUser.CompanyId,
            SaleRecordId = sale.Id,
            ReceiptNumber = sale.ReceiptNumber,
            Date = today,
            CreatedBy = currentUser.UserName,
            Reason = reason,
        };
        var computedRefund = 0m;

        foreach (var line in lines)
        {
            var item = sale.Items.FirstOrDefault(i => i.Id == line.SaleItemId)
                ?? throw new ArgumentException("Um dos itens informados não pertence a esta venda.");
            var remaining = item.Qty - returnedQty.GetValueOrDefault(item.Id);
            if (line.Qty > remaining + 0.0005m)
            {
                throw new InvalidOperationException(
                    $"{item.Name}: só restam {remaining.ToString("0.###", PtBr)} {item.Unit} para devolver (vendido {item.Qty.ToString("0.###", PtBr)}).");
            }

            var lineRefund = Math.Round(line.Qty * item.UnitPrice * paidFactor, 2, MidpointRounding.AwayFromZero);
            computedRefund += lineRefund;
            saleReturn.NetRevenueReturned += Math.Round(line.Qty * item.UnitPrice * netFactor, 2, MidpointRounding.AwayFromZero);
            saleReturn.CostReturned += Math.Round(line.Qty * item.UnitCost, 2, MidpointRounding.AwayFromZero);
            saleReturn.Items.Add(new SaleReturnItem
            {
                SaleItemId = item.Id, Name = item.Name, Qty = line.Qty, Unit = item.Unit, RefundAmount = lineRefund, Restocked = line.Restock,
            });

            ApplyStockForReturn(item, line, restockedQty.GetValueOrDefault(item.Id), kits, products, lots, sale, reason, now, today);
        }

        var totalRefund = request.RefundAmount is > 0 ? Math.Round(request.RefundAmount.Value, 2, MidpointRounding.AwayFromZero) : computedRefund;
        if (totalRefund <= 0) throw new ArgumentException("O valor devolvido deve ser maior que zero.");
        if (previousRefund + totalRefund > sale.Total + 0.005m)
        {
            throw new ArgumentException(
                $"O valor passa do que o cliente pagou: já foram devolvidos {previousRefund:C} de {sale.Total:C}.");
        }
        saleReturn.RefundAmount = totalRefund;

        // O valor devolvido abate primeiro o que o cliente ainda não pagou (da última parcela para a primeira); o que já foi
        // recebido sai do caixa.
        var open = await db.AccountsReceivables
            .Where(r => r.CompanyId == currentUser.CompanyId && r.SaleId == sale.SaleId && r.Status != "recebido")
            .OrderByDescending(r => r.InstallmentNumber)
            .ToListAsync(ct);
        var toAbate = totalRefund;
        foreach (var receivable in open)
        {
            if (toAbate <= 0) break;
            var take = Math.Min(toAbate, receivable.GrossAmount);
            var feeCut = receivable.GrossAmount > 0
                ? Math.Round(receivable.FeeRetention * take / receivable.GrossAmount, 2, MidpointRounding.AwayFromZero)
                : 0m;
            receivable.GrossAmount -= take;
            receivable.FeeRetention -= feeCut;
            receivable.NetAmount = receivable.GrossAmount - receivable.FeeRetention;
            if (receivable.GrossAmount <= 0) db.AccountsReceivables.Remove(receivable);
            toAbate -= take;
        }
        saleReturn.ReceivableAbated = totalRefund - toAbate;
        saleReturn.CashRefundAmount = toAbate;

        db.SaleReturns.Add(saleReturn);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(
            "devolveu", "Venda PDV",
            $"{sale.ReceiptNumber} • {totalRefund.ToString("C", PtBr)}",
            reason, ct);
        await transaction.CommitAsync(ct);

        var all = await db.SaleReturns.Include(r => r.Items)
            .Where(r => r.SaleRecordId == sale.Id && r.CompanyId == currentUser.CompanyId).ToListAsync(ct);
        return ToDto(sale, all);
    }

    /// <summary>
    /// Estoque de um item devolvido. Apto para revenda: volta ao produto e aos mesmos lotes de onde a venda tirou, com movimentação.
    /// Não apto: o estoque não volta; o custo vira perda. Kit: cada componente na proporção de kits devolvidos.
    /// </summary>
    private void ApplyStockForReturn(
        SaleItem item, ReturnSaleLine line, decimal alreadyRestocked, Dictionary<Guid, Kit> kits, Dictionary<Guid, Product> products,
        Dictionary<Guid, StockBatch> lots, SaleRecord sale, string reason, DateTime now, DateOnly today)
    {
        // (produto, quantidade, custo total) de tudo que voltou ou se perdeu neste item.
        var parts = new List<(Product Product, decimal Qty, decimal Cost, decimal AlreadyRestocked)>();
        if (item.ProductId.HasValue && products.TryGetValue(item.ProductId.Value, out var product))
        {
            parts.Add((product, line.Qty, line.Qty * item.UnitCost, alreadyRestocked));
        }
        else if (item.KitId.HasValue && kits.TryGetValue(item.KitId.Value, out var kit))
        {
            var weights = kit.Components
                .Where(c => products.ContainsKey(c.ProductId))
                .Select(c => (Component: c, Weight: c.Quantity * products[c.ProductId].CostPrice))
                .ToList();
            var totalWeight = weights.Sum(w => w.Weight);
            var kitCost = line.Qty * item.UnitCost;
            foreach (var (component, weight) in weights)
            {
                var share = totalWeight > 0 ? weight / totalWeight : 1m / weights.Count;
                parts.Add((products[component.ProductId], component.Quantity * line.Qty, kitCost * share, component.Quantity * alreadyRestocked));
            }
        }

        foreach (var (product2, qty, cost, restockedBefore) in parts)
        {
            if (line.Restock)
            {
                product2.Stock += qty;
                RestoreToLots(item, product2.Id, restockedBefore, qty, lots);
                db.StockMovements.Add(new StockMovement
                {
                    CompanyId = currentUser.CompanyId,
                    Timestamp = now,
                    Type = "devolucao_venda",
                    Title = $"Devolução da venda {sale.ReceiptNumber}: {product2.Name}",
                    DocInfo = $"Devolução da venda {sale.ReceiptNumber}",
                    AmountKg = qty,
                    Unit = product2.Unit,
                    Description = $"Devolução apta para revenda ({item.Name}): {reason}",
                    BadgeLabel = "Devolução",
                    AuditLink = sale.ReceiptNumber,
                });
            }
            else
            {
                // Não pode ser revendido: o estoque não volta (já tinha saído) e o custo entra nas perdas.
                db.StockLosses.Add(new StockLoss
                {
                    CompanyId = currentUser.CompanyId,
                    Date = today,
                    ProductId = product2.Id,
                    ProductName = product2.Name,
                    Qty = qty,
                    Unit = product2.Unit,
                    UnitCost = qty > 0 ? Math.Round(cost / qty, 4, MidpointRounding.AwayFromZero) : 0m,
                    TotalCost = Math.Round(cost, 2, MidpointRounding.AwayFromZero),
                    Reason = "outros",
                    Notes = $"Devolução da venda {sale.ReceiptNumber} sem condição de revenda: {reason}",
                    CreatedBy = currentUser.UserName,
                });
            }
        }
    }

    /// <summary>Devolve a quantidade aos lotes de onde o produto saiu nesta venda, pulando o que já foi devolvido antes.</summary>
    private static void RestoreToLots(SaleItem item, Guid productId, decimal alreadyRestocked, decimal qty, Dictionary<Guid, StockBatch> lots)
    {
        var skip = alreadyRestocked;
        var toRestore = qty;
        foreach (var consumption in item.Lots.Where(l => l.ProductId == productId))
        {
            var capacity = consumption.Qty;
            if (skip > 0)
            {
                var skipped = Math.Min(skip, capacity);
                skip -= skipped;
                capacity -= skipped;
            }
            if (capacity <= 0) continue;

            var give = Math.Min(capacity, toRestore);
            if (lots.TryGetValue(consumption.StockBatchId, out var lot)) lot.AvailableKg += give;
            toRestore -= give;
            if (toRestore <= 0) break;
        }
    }

    private static SaleHistoryDto ToDto(SaleRecord s, List<SaleReturn> returns)
    {
        var returnedByItem = returns.SelectMany(r => r.Items).GroupBy(i => i.SaleItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        return new SaleHistoryDto(
            s.Id, s.SaleId, s.ReceiptNumber, s.Timestamp, s.Total, s.NetAmount, s.FeeAmount, s.DiscountAmount,
            s.PaymentMode, s.CardFlag, s.ClientName, s.SoldBy, s.Status, s.CanceledAt, s.CanceledBy, s.CancelReason,
            s.Items.Sum(i => i.TotalCost),
            s.NetAmount - s.Items.Sum(i => i.TotalCost),
            s.Items.OrderBy(i => i.Name)
                .Select(i => new SaleItemDto(
                    i.Id, i.Name, i.Qty, i.Unit, i.UnitPrice, i.Subtotal, i.PriceKnown, i.UnitCost, i.TotalCost, i.CostEstimated,
                    returnedByItem.GetValueOrDefault(i.Id)))
                .ToList(),
            returns.OrderBy(r => r.CreatedAt)
                .Select(r => new SaleReturnDto(
                    r.Id, r.Date, r.CreatedBy, r.Reason, r.RefundAmount, r.ReceivableAbated, r.CashRefundAmount,
                    r.Items.Select(i => new SaleReturnLineDto(i.Name, i.Qty, i.Unit, i.RefundAmount, i.Restocked)).ToList()))
                .ToList(),
            returns.Sum(r => r.RefundAmount));
    }
}
