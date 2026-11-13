using GCStock.Application.Abstractions;
using GCStock.Application.Catalog;
using GCStock.Application.Dashboard;
using GCStock.Application.Finance;
using GCStock.Domain.Entities;
using GCStock.Domain.Services;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

/// <summary>
/// Indicadores do painel. Não tem regra própria de custo, margem ou resultado: reaproveita os serviços que já calculam
/// (resultado do período, caixa, kits e produtos), para o painel nunca divergir das telas de origem.
/// </summary>
public class DashboardService(
    GCStockDbContext db,
    ICurrentUserContext currentUser,
    IFinancialSummaryService financial,
    ICashService cash,
    IKitService kitService,
    IProductService productService) : IDashboardService
{
    private static readonly TimeSpan BrazilOffset = TimeSpan.FromHours(-3);
    private const int TurnoverDays = 30;
    private const int TopCount = 5;

    private sealed record SaleFact(DateOnly Date, decimal Total, SaleRecord Sale);
    private sealed record ReturnFact(DateOnly Date, decimal Amount);

    public async Task<DashboardDto> GetAsync(DateOnly reference, CancellationToken ct)
    {
        var today = ExpiryRules.Today(DateTime.UtcNow);
        var companyId = currentUser.CompanyId;

        var monthFrom = new DateOnly(reference.Year, reference.Month, 1);
        var monthTo = monthFrom.AddMonths(1).AddDays(-1);
        var (weekFrom, weekTo) = SalesGoal.WeekOf(reference);

        // Janela de vendas: do mês anterior (comparação) ou dos últimos 30 dias (giro), o que começar antes, até o fim do mês/semana.
        var windowFrom = new[] { monthFrom.AddMonths(-1), reference.AddDays(-TurnoverDays), weekFrom.AddDays(-7) }.Min();
        var windowTo = weekTo > monthTo ? weekTo : monthTo;
        var fromUtc = DateTime.SpecifyKind(windowFrom.ToDateTime(TimeOnly.MinValue) - BrazilOffset, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(windowTo.AddDays(1).ToDateTime(TimeOnly.MinValue) - BrazilOffset, DateTimeKind.Utc);

        var sales = (await db.SaleRecords
                .Include(s => s.Items)
                .Where(s => s.CompanyId == companyId && s.Status != "cancelada" && s.Timestamp >= fromUtc && s.Timestamp < toUtc)
                .ToListAsync(ct))
            .Select(s => new SaleFact(DateOnly.FromDateTime(s.Timestamp + BrazilOffset), s.Total, s))
            .ToList();
        var returns = (await db.SaleReturns
                .Where(r => r.CompanyId == companyId && r.Date >= windowFrom && r.Date <= windowTo)
                .Select(r => new { r.Date, r.RefundAmount })
                .ToListAsync(ct))
            .Select(r => new ReturnFact(r.Date, r.RefundAmount))
            .ToList();

        var settings = await db.AlertSettings.FirstOrDefaultAsync(a => a.CompanyId == companyId, ct);
        var goalTarget = settings?.MonthlyRevenueGoal ?? 3000m;

        var products = await productService.GetAllAsync(ct);
        var productCategories = await db.Products.Where(p => p.CompanyId == companyId).ToDictionaryAsync(p => p.Id, p => p.Category, ct);
        var kits = await kitService.GetAllAsync(ct);

        var commercial = BuildCommercial(sales, returns, reference, today, monthFrom, monthTo, weekFrom, weekTo, goalTarget, productCategories);
        var financialDash = await BuildFinancialAsync(reference, monthFrom, monthTo, ct);
        var stock = await BuildStockAsync(products, sales, kits, today, reference, ct);
        var kitDash = BuildKits(kits, sales, monthFrom, monthTo);

        return new DashboardDto(reference, commercial, financialDash, stock, kitDash);
    }

    // ---------------------------------------------------------------- Comercial

    private static PeriodRevenueDto Period(
        List<SaleFact> sales, List<ReturnFact> returns, DateOnly from, DateOnly to, DateOnly previousFrom, DateOnly previousTo)
    {
        (decimal Revenue, int Count) Sum(DateOnly a, DateOnly b)
        {
            var inRange = sales.Where(s => s.Date >= a && s.Date <= b).ToList();
            var refunded = returns.Where(r => r.Date >= a && r.Date <= b).Sum(r => r.Amount);
            return (inRange.Sum(s => s.Total) - refunded, inRange.Count);
        }

        var current = Sum(from, to);
        var previous = Sum(previousFrom, previousTo);
        return new PeriodRevenueDto(
            from, to, current.Revenue, previous.Revenue, SalesGoal.ChangePercent(current.Revenue, previous.Revenue), current.Count, previous.Count);
    }

    private static CommercialDto BuildCommercial(
        List<SaleFact> sales, List<ReturnFact> returns, DateOnly reference, DateOnly today, DateOnly monthFrom, DateOnly monthTo,
        DateOnly weekFrom, DateOnly weekTo, decimal goalTarget, Dictionary<Guid, string> categories)
    {
        var day = Period(sales, returns, reference, reference, reference.AddDays(-1), reference.AddDays(-1));
        var week = Period(sales, returns, weekFrom, weekTo, weekFrom.AddDays(-7), weekTo.AddDays(-7));
        var prevMonthFrom = monthFrom.AddMonths(-1);
        var month = Period(sales, returns, monthFrom, monthTo, prevMonthFrom, monthFrom.AddDays(-1));

        var monthSales = sales.Where(s => s.Date >= monthFrom && s.Date <= monthTo).ToList();
        var previousMonthSales = sales.Where(s => s.Date >= prevMonthFrom && s.Date < monthFrom).ToList();
        var monthItems = monthSales.SelectMany(s => s.Sale.Items).ToList();

        decimal Average(PeriodRevenueDto p, decimal revenue) => p.SalesCount > 0 ? Math.Round(revenue / p.SalesCount, 2, MidpointRounding.AwayFromZero) : 0m;

        List<RankedItemDto> Top(bool kits) => monthItems
            .Where(i => i.KitId.HasValue == kits)
            .GroupBy(i => (i.Name, i.Unit))
            .Select(g => new RankedItemDto(g.Key.Name, g.Key.Unit, g.Sum(i => i.Qty), g.Sum(i => i.Subtotal)))
            .OrderByDescending(x => x.Qty)
            .ThenByDescending(x => x.Revenue)
            .Take(TopCount)
            .ToList();

        var byCategory = monthItems
            .GroupBy(i => i.KitId.HasValue ? "Kits de Churrasco" : i.ProductId.HasValue && categories.TryGetValue(i.ProductId.Value, out var c) && !string.IsNullOrWhiteSpace(c) ? c : "Sem categoria")
            .Select(g => new CategoryRevenueDto(g.Key, g.Sum(i => i.Subtotal)))
            .OrderByDescending(x => x.Revenue)
            .ToList();

        return new CommercialDto(
            day, week, month,
            ToGoal(SalesGoal.Compute(goalTarget, month.Revenue, reference, today)),
            (int)Math.Round(monthItems.Where(i => i.KitId.HasValue).Sum(i => i.Qty), MidpointRounding.AwayFromZero),
            Average(month, month.Revenue),
            Average(new PeriodRevenueDto(prevMonthFrom, monthFrom.AddDays(-1), month.PreviousRevenue, 0, null, month.PreviousSalesCount, 0), previousMonthSales.Sum(s => s.Total)),
            Top(false), Top(true), byCategory);
    }

    private static GoalDto ToGoal(SalesGoal.Progress p) =>
        new(p.Target, p.Realized, p.Remaining, p.PercentAchieved, p.Projection, p.DaysElapsed, p.DaysInMonth);

    // ---------------------------------------------------------------- Financeiro

    private async Task<FinancialDashDto> BuildFinancialAsync(DateOnly reference, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var summary = await financial.GetAsync(from, to, ct);
        var cashSummary = await cash.GetAsync(from, to, ct);
        var monthly = await financial.GetMonthlyAsync(reference.Year, ct);

        return new FinancialDashDto(
            from, to,
            summary.GrossRevenue,
            summary.CostOfGoodsSold,
            summary.GrossProfit,
            summary.GrossMarginPercent,
            summary.OperatingExpenses + summary.Payroll + summary.Losses + summary.FinancialExpenses,
            summary.NetResult,
            cashSummary.CurrentBalance,
            cashSummary.ToPay,
            cashSummary.OverduePayable,
            cashSummary.ToReceive,
            monthly.Select(m => new MonthPointDto(m.Month, m.NetRevenue, m.NetResult)).ToList());
    }

    // ---------------------------------------------------------------- Estoque

    private async Task<StockDashDto> BuildStockAsync(
        List<ProductDto> products, List<SaleFact> sales, List<KitDto> kits, DateOnly today, DateOnly reference, CancellationToken ct)
    {
        StockAlertItemDto Alert(ProductDto p, decimal qty = 0m) =>
            new(p.Id, p.Name, p.Unit, p.Stock, p.MinStock, p.ExpiryDaysRemaining, qty);

        var low = products.Where(p => p.Stock > 0 && p.Stock < p.MinStock).OrderBy(p => p.Stock / Math.Max(p.MinStock, 1m)).ToList();
        var outOfStock = products.Where(p => p.Stock <= 0).ToList();
        var nearExpiry = products.Where(p => p.Stock > 0 && p.ExpiryStatus == "critico").OrderBy(p => p.ExpiryDaysRemaining).ToList();

        // Produto com lote vencido e saldo: a quantidade vencida vem dos lotes (o status do produto só olha o lote que vence antes).
        var expiredByProduct = (await db.StockBatches
                .Where(b => b.CompanyId == currentUser.CompanyId && b.AvailableKg > 0 && b.ExpiryDate < today)
                .Select(b => new { b.ProductId, b.AvailableKg })
                .ToListAsync(ct))
            .GroupBy(b => b.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(b => b.AvailableKg));
        var expired = products.Where(p => expiredByProduct.ContainsKey(p.Id)).ToList();

        // Giro dos últimos 30 dias: o que saiu avulso e dentro de kits (pela composição atual dos kits).
        var since = reference.AddDays(-TurnoverDays);
        var componentsByKit = kits.ToDictionary(k => k.Id, k => k.Components);
        var soldByProduct = new Dictionary<Guid, decimal>();
        foreach (var item in sales.Where(s => s.Date > since && s.Date <= reference).SelectMany(s => s.Sale.Items))
        {
            if (item.ProductId.HasValue)
            {
                soldByProduct[item.ProductId.Value] = soldByProduct.GetValueOrDefault(item.ProductId.Value) + item.Qty;
            }
            else if (item.KitId.HasValue && componentsByKit.TryGetValue(item.KitId.Value, out var components))
            {
                foreach (var component in components)
                {
                    soldByProduct[component.ProductId] = soldByProduct.GetValueOrDefault(component.ProductId) + component.Quantity * item.Qty;
                }
            }
        }

        var turnover = products
            .Where(p => soldByProduct.ContainsKey(p.Id))
            .OrderByDescending(p => soldByProduct[p.Id])
            .Take(TopCount)
            .Select(p => new TurnoverItemDto(p.Name, p.Unit, soldByProduct[p.Id], p.Stock))
            .ToList();
        var stagnant = products
            .Where(p => p.Stock > 0 && !soldByProduct.ContainsKey(p.Id))
            .OrderByDescending(p => p.Stock * p.CostPrice)
            .ToList();

        return new StockDashDto(
            low.Count, low.Take(TopCount).Select(p => Alert(p)).ToList(),
            outOfStock.Count, outOfStock.Take(TopCount).Select(p => Alert(p)).ToList(),
            nearExpiry.Count, nearExpiry.Take(TopCount).Select(p => Alert(p)).ToList(),
            expired.Count, expired.Take(TopCount).Select(p => Alert(p, expiredByProduct[p.Id])).ToList(),
            Math.Round(products.Where(p => p.Stock > 0).Sum(p => p.Stock * p.CostPrice), 2, MidpointRounding.AwayFromZero),
            turnover,
            stagnant.Count,
            stagnant.Take(TopCount).Select(p => new TurnoverItemDto(p.Name, p.Unit, 0m, p.Stock)).ToList());
    }

    // ---------------------------------------------------------------- Kits

    private static KitDashDto BuildKits(List<KitDto> kits, List<SaleFact> sales, DateOnly monthFrom, DateOnly monthTo)
    {
        var rows = kits
            .Select(k => new KitRowDto(
                k.Id, k.Name, k.CostPrice, k.SuggestedPrice, k.MarginPercent, k.MinMarginPercent, k.IsBelowMinMargin,
                k.PriceIncreaseNeeded, k.TotalWeightKg, k.StockAvailable, k.StockStatus))
            .OrderBy(k => k.Name)
            .ToList();

        // Vendas do mês por kit: quantidade, receita dos itens e lucro (receita dos itens menos o custo gravado na venda).
        var kitIds = kits.Select(k => k.Id).ToHashSet();
        var ranked = sales
            .Where(s => s.Date >= monthFrom && s.Date <= monthTo)
            .SelectMany(s => s.Sale.Items)
            .Where(i => i.KitId.HasValue && kitIds.Contains(i.KitId.Value))
            .GroupBy(i => (Id: i.KitId!.Value, i.Name))
            .Select(g => new KitRankDto(g.Key.Name, g.Sum(i => i.Qty), g.Sum(i => i.Subtotal), g.Sum(i => i.Subtotal - i.TotalCost)))
            .ToList();

        var drivers = kits
            .SelectMany(k => k.Components)
            .GroupBy(c => c.Name)
            .Select(g => (Product: g.Key, Amount: g.Sum(c => c.Subtotal)))
            .ToList();
        var totalCost = drivers.Sum(d => d.Amount);

        return new KitDashDto(
            rows,
            rows.Count(r => r.IsBelowMinMargin),
            ranked.OrderByDescending(r => r.Qty).FirstOrDefault(),
            ranked.OrderByDescending(r => r.Profit).FirstOrDefault(),
            drivers
                .OrderByDescending(d => d.Amount)
                .Take(TopCount)
                .Select(d => new CostDriverDto(d.Product, d.Amount, totalCost > 0 ? Math.Round(d.Amount / totalCost * 100m, 1, MidpointRounding.AwayFromZero) : 0m))
                .ToList());
    }
}
