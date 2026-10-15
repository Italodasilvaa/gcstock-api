using GCStock.Application.Abstractions;
using GCStock.Application.Finance;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

public class FinancialSummaryService(GCStockDbContext db, ICurrentUserContext currentUser) : IFinancialSummaryService
{
    // Horário de Brasília (UTC-3, sem horário de verão): o dia da venda é o dia local, não o do servidor (UTC).
    private static readonly TimeSpan BrazilOffset = TimeSpan.FromHours(-3);

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

        var netResult = grossProfit - operatingExpenses - payroll;

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
            netResult,
            capitalExpenses,
            expenses,
            stockValue,
            productProfits,
            completed.SelectMany(s => s.Items).Count(i => i.CostEstimated));
    }
}
