using System.Globalization;
using GCStock.Application.Abstractions;
using GCStock.Application.Audit;
using GCStock.Application.Sales;
using GCStock.Domain.Entities;
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

        return sales.Select(ToDto).ToList();
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

        // O título a receber da venda (cartão ou a prazo) é identificado pelo número do cupom.
        var receivable = string.IsNullOrEmpty(sale.ReceiptNumber)
            ? null
            : await db.AccountsReceivables.FirstOrDefaultAsync(
                r => r.CompanyId == currentUser.CompanyId && r.Reference == sale.ReceiptNumber, ct);
        if (receivable?.Status == "recebido")
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
                Type = "entrada",
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
        if (receivable is not null) db.AccountsReceivables.Remove(receivable);

        if (sale.PaymentMode == "a_prazo" && sale.ClientId.HasValue)
        {
            var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == sale.ClientId && c.CompanyId == currentUser.CompanyId, ct);
            if (client is not null) client.TotalSpent = Math.Max(0, client.TotalSpent - sale.Total);
        }

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
        return new CancelSaleResult(ToDto(sale), notice);
    }

    private static SaleHistoryDto ToDto(SaleRecord s) => new(
        s.Id, s.SaleId, s.ReceiptNumber, s.Timestamp, s.Total, s.NetAmount, s.FeeAmount, s.DiscountAmount,
        s.PaymentMode, s.CardFlag, s.ClientName, s.SoldBy, s.Status, s.CanceledAt, s.CanceledBy, s.CancelReason,
        s.Items.Sum(i => i.TotalCost),
        s.NetAmount - s.Items.Sum(i => i.TotalCost),
        s.Items.OrderBy(i => i.Name)
            .Select(i => new SaleItemDto(i.Id, i.Name, i.Qty, i.Unit, i.UnitPrice, i.Subtotal, i.PriceKnown, i.UnitCost, i.TotalCost, i.CostEstimated))
            .ToList());
}
