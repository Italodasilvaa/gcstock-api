using GCStock.Application.Abstractions;
using GCStock.Application.Finance;
using GCStock.Application.Inventory;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

public class FinancialSummaryService(GCStockDbContext db, ICurrentUserContext currentUser) : IFinancialSummaryService
{
    // Horário de Brasília (UTC-3, sem horário de verão): o dia da venda é o dia local, não o do servidor (UTC).
    private static readonly TimeSpan BrazilOffset = TimeSpan.FromHours(-3);

    public async Task<List<MonthlyBalanceDto>> GetMonthlyAsync(int year, CancellationToken ct)
    {
        var months = new List<MonthlyBalanceDto>();
        for (var month = 1; month <= 12; month++)
        {
            var first = new DateOnly(year, month, 1);
            var s = await GetAsync(first, first.AddMonths(1).AddDays(-1), ct);
            months.Add(new MonthlyBalanceDto(
                month, s.SalesCount, s.NetRevenue, s.CostOfGoodsSold, s.GrossProfit, s.OperatingExpenses,
                s.Payroll, s.Losses, s.NetResult, s.PurchasesTotal, s.CapitalExpenses));
        }
        return months;
    }

    public async Task<List<LotProfitDto>> GetLotProfitsAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from) (from, to) = (to, from);
        var companyId = currentUser.CompanyId;
        var fromUtc = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue) - BrazilOffset, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(to.AddDays(1).ToDateTime(TimeOnly.MinValue) - BrazilOffset, DateTimeKind.Utc);

        var lots = await db.StockBatches.Where(b => b.CompanyId == companyId).ToListAsync(ct);

        var sales = await db.SaleRecords
            .Include(s => s.Items).ThenInclude(i => i.Lots)
            .Where(s => s.CompanyId == companyId && s.Status != "cancelada" && s.Timestamp >= fromUtc && s.Timestamp < toUtc)
            .ToListAsync(ct);

        // Receita e quantidade vendida por lote. A receita é a líquida da venda (já sem desconto e taxa), repartida
        // pelos itens; kits ficam de fora (o preço do kit não se reparte por lote de componente).
        var soldQty = new Dictionary<Guid, decimal>();
        var revenue = new Dictionary<Guid, decimal>();
        var estimatedCost = new Dictionary<Guid, decimal>();
        foreach (var sale in sales)
        {
            var itemsSubtotal = sale.Items.Sum(i => i.Subtotal);
            var netFactor = itemsSubtotal > 0 ? sale.NetAmount / itemsSubtotal : 1m;
            foreach (var item in sale.Items.Where(i => i.ProductId.HasValue && i.Qty > 0))
            {
                foreach (var consumed in item.Lots)
                {
                    soldQty[consumed.StockBatchId] = soldQty.GetValueOrDefault(consumed.StockBatchId) + consumed.Qty;
                    revenue[consumed.StockBatchId] = revenue.GetValueOrDefault(consumed.StockBatchId) + consumed.Qty * item.UnitPrice * netFactor;
                    estimatedCost[consumed.StockBatchId] = estimatedCost.GetValueOrDefault(consumed.StockBatchId) + consumed.Qty * item.UnitCost;
                }
            }
        }

        var lossLots = await db.StockLossLots
            .Where(l => l.StockLoss!.CompanyId == companyId && l.StockLoss.Status == "ativa" && l.StockLoss.Date >= from && l.StockLoss.Date <= to)
            .Select(l => new { l.StockBatchId, l.Qty, l.StockLoss!.UnitCost })
            .ToListAsync(ct);
        var lostQty = lossLots.GroupBy(l => l.StockBatchId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        var lostCost = lossLots.GroupBy(l => l.StockBatchId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty * x.UnitCost));

        var result = new List<LotProfitDto>();
        foreach (var lot in lots)
        {
            var sold = soldQty.GetValueOrDefault(lot.Id);
            var lost = lostQty.GetValueOrDefault(lot.Id);
            if (sold == 0 && lost == 0 && lot.AvailableKg <= 0) continue;

            var rev = Math.Round(revenue.GetValueOrDefault(lot.Id), 2, MidpointRounding.AwayFromZero);
            // Custo do lote (o que foi pago); lote antigo sem custo registrado usa o custo guardado nas vendas.
            var cost = Math.Round(lot.UnitCost > 0 ? sold * lot.UnitCost : estimatedCost.GetValueOrDefault(lot.Id), 2, MidpointRounding.AwayFromZero);
            var lossCost = Math.Round(lostCost.GetValueOrDefault(lot.Id), 2, MidpointRounding.AwayFromZero);
            var profit = rev - cost - lossCost;
            result.Add(new LotProfitDto(
                lot.Id, lot.LotNumber, lot.ProductId, lot.ProductName, lot.Supplier, lot.Unit, lot.EntryDate, lot.ExpiryDate,
                lot.UnitCost, lot.AvailableKg + sold + lost, sold, lost, lot.AvailableKg,
                rev, cost, lossCost, profit,
                rev > 0 ? Math.Round(profit / rev * 100, 1) : 0,
                Math.Round(lot.AvailableKg * lot.UnitCost, 2, MidpointRounding.AwayFromZero)));
        }

        return result.OrderByDescending(l => l.EntryDate).ThenBy(l => l.ProductName).ToList();
    }

    public async Task<FinancialSummaryDto> GetAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from) (from, to) = (to, from);

        var companyId = currentUser.CompanyId;
        var fromUtc = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue) - BrazilOffset, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(to.AddDays(1).ToDateTime(TimeOnly.MinValue) - BrazilOffset, DateTimeKind.Utc);

        // ---------- Vendas (canceladas não contam)
        var sales = await db.SaleRecords
            .Include(s => s.Items)
            .Where(s => s.CompanyId == companyId && s.Timestamp >= fromUtc && s.Timestamp < toUtc)
            .ToListAsync(ct);
        var completed = sales.Where(s => s.Status != "cancelada").ToList();

        var grossRevenue = completed.Sum(s => s.Total);
        var fees = completed.Sum(s => s.FeeAmount);
        var discounts = completed.Sum(s => s.DiscountAmount);
        var netRevenue = completed.Sum(s => s.NetAmount);
        var costOfGoodsSold = completed.Sum(s => s.Items.Sum(i => i.TotalCost));
        var grossProfit = netRevenue - costOfGoodsSold;

        // ---------- Entradas (compras)
        var purchases = await db.PurchaseEntries
            .Where(p => p.CompanyId == companyId && p.EntryDate >= from && p.EntryDate <= to)
            .Select(p => new { p.SupplierName, p.TotalAmount, p.ShippingCost, p.PaymentStatus })
            .ToListAsync(ct);

        var topSuppliers = purchases
            .GroupBy(p => string.IsNullOrWhiteSpace(p.SupplierName) ? "(sem fornecedor)" : p.SupplierName)
            .Select(g => new SupplierSpendDto(g.Key, g.Count(), g.Sum(p => p.TotalAmount)))
            .OrderByDescending(x => x.Amount)
            .Take(5)
            .ToList();

        // ---------- Despesas (contas a pagar que não são compras de mercadoria) e folha
        var payables = await db.AccountsPayables
            .Where(p => p.CompanyId == companyId && p.Category != "compras" && p.IssueDate >= from && p.IssueDate <= to)
            .Select(p => new { p.Category, p.Amount })
            .ToListAsync(ct);

        var expenses = payables
            .GroupBy(p => p.Category)
            .Select(g => new ExpenseCategoryDto(g.Key, g.Sum(p => p.Amount)))
            .OrderByDescending(x => x.Amount)
            .ToList();
        var operatingExpenses = payables.Where(p => p.Category == "outros").Sum(p => p.Amount);
        var capitalExpenses = payables.Where(p => p.Category is "equipamento" or "investimento").Sum(p => p.Amount);

        var monthlyPayroll = await db.Employees
            .Where(e => e.CompanyId == companyId && e.Active)
            .SumAsync(e => (decimal?)e.Salary, ct) ?? 0m;
        var monthsTouched = (to.Year - from.Year) * 12 + to.Month - from.Month + 1;
        var payroll = monthlyPayroll * monthsTouched;

        // ---------- Perdas de estoque (desfeitas não contam)
        var losses = await db.StockLosses
            .Where(l => l.CompanyId == companyId && l.Status == "ativa" && l.Date >= from && l.Date <= to)
            .Select(l => new { l.Reason, l.TotalCost })
            .ToListAsync(ct);
        var lossesTotal = losses.Sum(l => l.TotalCost);
        var lossesByReason = losses
            .GroupBy(l => l.Reason)
            .Select(g => new LossReasonDto(g.Key, LossReasons.Label(g.Key), g.Count(), g.Sum(x => x.TotalCost)))
            .OrderByDescending(x => x.Amount)
            .ToList();

        // ---------- Valor do estoque ao custo médio atual
        var stockValue = await db.Products
            .Where(p => p.CompanyId == companyId && p.Stock > 0)
            .SumAsync(p => (decimal?)(p.Stock * p.CostPrice), ct) ?? 0m;

        // ---------- Lucro por produto (itens com preço conhecido)
        var productProfits = completed
            .SelectMany(s => s.Items)
            .Where(i => i.PriceKnown)
            .GroupBy(i => (Key: i.ProductId ?? i.KitId ?? Guid.Empty, i.Name))
            .Select(g =>
            {
                var revenue = g.Sum(i => i.Subtotal);
                var cost = g.Sum(i => i.TotalCost);
                return new ProductProfitDto(
                    g.Key.Name, g.First().Unit, g.Sum(i => i.Qty), revenue, cost, revenue - cost,
                    revenue > 0 ? Math.Round((revenue - cost) / revenue * 100, 1) : 0,
                    g.Any(i => i.CostEstimated));
            })
            .OrderByDescending(x => x.Profit)
            .ToList();

        var netResult = grossProfit - operatingExpenses - payroll - lossesTotal;

        return new FinancialSummaryDto(
            from, to,
            purchases.Count,
            purchases.Sum(p => p.TotalAmount),
            purchases.Where(p => p.PaymentStatus == "pago").Sum(p => p.TotalAmount),
            purchases.Where(p => p.PaymentStatus != "pago").Sum(p => p.TotalAmount),
            purchases.Sum(p => p.ShippingCost),
            topSuppliers,
            completed.Count,
            sales.Count - completed.Count,
            grossRevenue,
            discounts,
            fees,
            netRevenue,
            costOfGoodsSold,
            grossProfit,
            netRevenue > 0 ? Math.Round(grossProfit / netRevenue * 100, 1) : 0,
            operatingExpenses,
            payroll,
            lossesTotal,
            losses.Count,
            lossesByReason,
            netResult,
            capitalExpenses,
            expenses,
            stockValue,
            productProfits,
            completed.SelectMany(s => s.Items).Count(i => i.CostEstimated));
    }
}
