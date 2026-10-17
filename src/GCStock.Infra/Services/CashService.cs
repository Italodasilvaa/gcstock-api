using GCStock.Application.Abstractions;
using GCStock.Application.Audit;
using GCStock.Application.Finance;
using GCStock.Domain.Entities;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

public class CashService(GCStockDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit) : ICashService
{
    // Horário de Brasília (UTC-3): o dia da venda é o dia local.
    private static readonly TimeSpan BrazilOffset = TimeSpan.FromHours(-3);
    private static readonly string[] CardOrCreditModes = ["credito_vista", "parcelado", "a_prazo"];

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow + BrazilOffset);

    public async Task<CashSummaryDto> GetAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from) (from, to) = (to, from);
        var companyId = currentUser.CompanyId;
        var today = Today();

        var movements = new List<CashMovementDto>();

        // Entradas: aportes
        var contributions = await db.CapitalContributions.Where(c => c.CompanyId == companyId).ToListAsync(ct);
        movements.AddRange(contributions.Select(c =>
            new CashMovementDto(c.Date, "aporte", string.IsNullOrWhiteSpace(c.Source) ? c.Description : $"{c.Description} ({c.Source})", c.Amount)));

        // Entradas: vendas recebidas na hora (cartão de crédito e fiado entram quando o título é liquidado)
        var sales = await db.SaleRecords
            .Where(s => s.CompanyId == companyId && s.Status != "cancelada" && !CardOrCreditModes.Contains(s.PaymentMode))
            .Select(s => new { s.Timestamp, s.ReceiptNumber, s.PaymentMode, s.NetAmount })
            .ToListAsync(ct);
        movements.AddRange(sales.Select(s =>
            new CashMovementDto(DateOnly.FromDateTime(s.Timestamp + BrazilOffset), "venda", $"Venda {s.ReceiptNumber} ({s.PaymentMode})", s.NetAmount)));

        // Entradas: títulos a receber liquidados
        var received = await db.AccountsReceivables
            .Where(r => r.CompanyId == companyId && r.Status == "recebido")
            .Select(r => new { r.ReceivedDate, r.DueDate, r.ClientOrOrigin, r.Reference, r.NetAmount })
            .ToListAsync(ct);
        movements.AddRange(received.Select(r =>
            new CashMovementDto(r.ReceivedDate ?? r.DueDate, "recebimento", $"Recebido: {r.ClientOrOrigin} ({r.Reference})", r.NetAmount)));

        // Saídas: toda nota de entrada (compra) sai do caixa na data da entrada, mesmo a prazo ou ainda não paga.
        var purchases = await db.PurchaseEntries
            .Where(p => p.CompanyId == companyId)
            .Select(p => new { p.EntryDate, p.OrderCode, p.SupplierName, p.TotalAmount, p.PaymentStatus })
            .ToListAsync(ct);
        movements.AddRange(purchases.Select(p =>
            new CashMovementDto(
                p.EntryDate, "compra",
                $"Compra {p.OrderCode} ({p.SupplierName})" + (p.PaymentStatus == "pago" ? "" : " — a pagar"),
                -p.TotalAmount)));

        // Saídas: equipamentos e despesas quando pagos (as compras já saíram acima, na entrada)
        var payables = await db.AccountsPayables.Where(p => p.CompanyId == companyId).ToListAsync(ct);
        movements.AddRange(payables.Where(p => p.Status == "pago" && p.Category != "compras").Select(p =>
            new CashMovementDto(
                p.PaidDate ?? p.IssueDate,
                p.Category == "equipamento" ? "equipamento" : "despesa",
                string.IsNullOrWhiteSpace(p.Supplier) ? p.Description : $"{p.Description} ({p.Supplier})",
                -p.Amount)));

        decimal Sum(Func<CashMovementDto, bool> filter) => movements.Where(filter).Sum(m => m.Amount);
        bool InPeriod(CashMovementDto m) => m.Date >= from && m.Date <= to;

        var openingBalance = Sum(m => m.Date < from);
        var contributionsIn = Sum(m => InPeriod(m) && m.Kind == "aporte");
        var salesIn = Sum(m => InPeriod(m) && m.Kind == "venda");
        var receivedIn = Sum(m => InPeriod(m) && m.Kind == "recebimento");
        var purchasesOut = -Sum(m => InPeriod(m) && m.Kind == "compra");
        var equipmentOut = -Sum(m => InPeriod(m) && m.Kind == "equipamento");
        var expensesOut = -Sum(m => InPeriod(m) && m.Kind == "despesa");

        var toReceive = await db.AccountsReceivables
            .Where(r => r.CompanyId == companyId && r.Status != "recebido")
            .SumAsync(r => (decimal?)r.NetAmount, ct) ?? 0m;
        var open = payables.Where(p => p.Status != "pago").ToList();
        var stockValue = await db.Products
            .Where(p => p.CompanyId == companyId && p.Stock > 0)
            .SumAsync(p => (decimal?)(p.Stock * p.CostPrice), ct) ?? 0m;
        var payroll = await db.Employees
            .Where(e => e.CompanyId == companyId && e.Active)
            .SumAsync(e => (decimal?)e.Salary, ct) ?? 0m;

        return new CashSummaryDto(
            from, to,
            openingBalance,
            contributionsIn, salesIn, receivedIn, contributionsIn + salesIn + receivedIn,
            purchasesOut, equipmentOut, expensesOut, purchasesOut + equipmentOut + expensesOut,
            openingBalance + contributionsIn + salesIn + receivedIn - purchasesOut - equipmentOut - expensesOut,
            Sum(m => m.Date <= today),
            toReceive,
            open.Sum(p => p.Amount),
            open.Where(p => p.Status == "vencido" || p.DueDate < today).Sum(p => p.Amount),
            open.Where(p => p.Category == "compras").Sum(p => p.Amount),
            stockValue,
            payroll,
            [.. movements.Where(InPeriod).OrderByDescending(m => m.Date).ThenBy(m => m.Kind)]);
    }

    public async Task<List<CapitalContributionDto>> GetContributionsAsync(CancellationToken ct) =>
        await db.CapitalContributions
            .Where(c => c.CompanyId == currentUser.CompanyId)
            .OrderByDescending(c => c.Date).ThenByDescending(c => c.CreatedAt)
            .Select(c => new CapitalContributionDto(c.Id, c.Date, c.Description, c.Source, c.Amount, c.CreatedBy))
            .ToListAsync(ct);

    public async Task<CapitalContributionDto> AddContributionAsync(AddCapitalContributionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Description)) throw new ArgumentException("Informe uma descrição para o aporte.");
        if (request.Amount <= 0) throw new ArgumentException("O valor do aporte deve ser maior que zero.");
        var date = request.Date ?? Today();
        if (date > Today()) throw new ArgumentException("A data do aporte não pode ser no futuro.");

        var saved = new CapitalContribution
        {
            CompanyId = currentUser.CompanyId,
            Date = date,
            Description = request.Description.Trim(),
            Source = request.Source?.Trim() ?? string.Empty,
            Amount = Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero),
            CreatedBy = currentUser.UserName,
        };
        db.CapitalContributions.Add(saved);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("criou", "Aporte de capital", $"{saved.Description} • {saved.Amount:0.00}", saved.Source, ct);
        return new CapitalContributionDto(saved.Id, saved.Date, saved.Description, saved.Source, saved.Amount, saved.CreatedBy);
    }

    public async Task<CapitalContributionDto?> UpdateContributionAsync(Guid id, AddCapitalContributionRequest request, CancellationToken ct)
    {
        var item = await db.CapitalContributions.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == currentUser.CompanyId, ct);
        if (item is null) return null;
        if (string.IsNullOrWhiteSpace(request.Description)) throw new ArgumentException("Informe uma descrição para o aporte.");
        if (request.Amount <= 0) throw new ArgumentException("O valor do aporte deve ser maior que zero.");
        var date = request.Date ?? item.Date;
        if (date > Today()) throw new ArgumentException("A data do aporte não pode ser no futuro.");

        item.Description = request.Description.Trim();
        item.Source = request.Source?.Trim() ?? string.Empty;
        item.Amount = Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        item.Date = date;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("editou", "Aporte de capital", $"{item.Description} • {item.Amount:0.00}", item.Source, ct);
        return new CapitalContributionDto(item.Id, item.Date, item.Description, item.Source, item.Amount, item.CreatedBy);
    }

    public async Task<bool> DeleteContributionAsync(Guid id, CancellationToken ct)
    {
        var item = await db.CapitalContributions.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == currentUser.CompanyId, ct);
        if (item is null) return false;
        db.CapitalContributions.Remove(item);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("excluiu", "Aporte de capital", $"{item.Description} • {item.Amount:0.00}", item.Source, ct);
        return true;
    }
}
