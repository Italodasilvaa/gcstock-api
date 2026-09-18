using Girus.Application.Audit;
using Girus.Application.Abstractions;
using Girus.Application.Common;
using Girus.Application.Finance;
using Girus.Domain.Entities;
using Girus.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace Girus.Infra.Services;

public class PayableService(GirusDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit) : IPayableService
{
    public async Task<List<AccountsPayableDto>> GetAllAsync(CancellationToken ct)
    {
        return await db.AccountsPayables
            .Where(p => p.CompanyId == currentUser.CompanyId)
            .OrderBy(p => p.DueDate)
            .Select(p => ToDto(p))
            .ToListAsync(ct);
    }

    public async Task<AccountsPayableDto> AddAsync(AddPayableRequest request, CancellationToken ct)
    {
        var dueDate = request.DueDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
        var saved = new AccountsPayable
        {
            CompanyId = currentUser.CompanyId,
            Supplier = request.Supplier ?? "Fornecedor Avulso",
            Description = request.Description ?? "Despesa Operacional",
            NfeReference = request.NfeReference ?? "Doc " + Random.Shared.Next(100, 1000),
            IssueDate = request.IssueDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            DueDate = dueDate,
            DueLabel = request.DueLabel ?? DateLabelHelper.DueLabelFor(dueDate),
            Amount = request.Amount ?? 100,
            PaymentMethod = request.PaymentMethod ?? "Boleto Bancário",
            Origin = request.Origin ?? "Despesa Manual",
            Status = "a_vencer",
        };
        db.AccountsPayables.Add(saved);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("criou", "Conta a Pagar", $"{saved.Supplier} • {saved.Description}", null, ct);
        return ToDto(saved);
    }

    public async Task<AccountsPayableDto?> SettleAsync(string id, SettlePayableRequest request, CancellationToken ct)
    {
        var item = await db.AccountsPayables.FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == currentUser.CompanyId, ct);
        if (item is null) return null;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        item.Status = "pago";
        item.PaidDate = today;
        item.DueLabel = $"Pago em {today:dd/MM/yyyy}";
        item.ReceiptAttached = true;

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("baixou", "Conta a Pagar", $"{item.Supplier} • {item.Description}", null, ct);
        return ToDto(item);
    }

    private static AccountsPayableDto ToDto(AccountsPayable p) => new(
        p.Id, p.Supplier, p.Description, p.NfeReference, p.IssueDate, p.DueDate, p.DueLabel, p.Amount,
        p.PaymentMethod, p.Origin, p.Status, p.PaidDate, p.ReceiptAttached);
}
