using GCStock.Application.Audit;
using GCStock.Application.Abstractions;
using GCStock.Application.Common;
using GCStock.Application.Finance;
using GCStock.Domain.Entities;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

public class PayableService(GCStockDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit) : IPayableService
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
        var dueDate = request.DueDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3).AddDays(30));
        var saved = new AccountsPayable
        {
            CompanyId = currentUser.CompanyId,
            Supplier = request.Supplier ?? "Fornecedor Avulso",
            Description = request.Description ?? "Despesa Operacional",
            NfeReference = request.NfeReference ?? "Doc " + Random.Shared.Next(100, 1000),
            IssueDate = request.IssueDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3)),
            DueDate = dueDate,
            DueLabel = request.DueLabel ?? DateLabelHelper.DueLabelFor(dueDate),
            Amount = request.Amount ?? 100,
            PaymentMethod = request.PaymentMethod ?? "Boleto Bancário",
            Origin = request.Origin ?? "Despesa Manual",
            Status = "a_vencer",
            Category = request.Category ?? "outros",
        };
        db.AccountsPayables.Add(saved);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("criou", "Conta a Pagar", $"{saved.Supplier} • {saved.Description}", null, ct);
        return ToDto(saved);
    }

    public async Task<AccountsPayableDto?> UpdateAsync(Guid id, UpdatePayableRequest request, CancellationToken ct)
    {
        var item = await db.AccountsPayables.FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == currentUser.CompanyId, ct);
        if (item is null) return null;
        if (item.Category == "compras" || item.PurchaseEntryId.HasValue)
        {
            throw new ArgumentException("Esta conta veio de uma compra: edite o pedido em Compras / Entradas.");
        }
        if (request.Amount is <= 0) throw new ArgumentException("O valor deve ser maior que zero.");
        if (request.Category is not null and not ("equipamento" or "outros"))
        {
            throw new ArgumentException("Categoria inválida: use 'equipamento' ou 'outros'.");
        }
        if (request.Description is not null && string.IsNullOrWhiteSpace(request.Description))
        {
            throw new ArgumentException("Informe uma descrição.");
        }

        if (request.Supplier is not null) item.Supplier = request.Supplier.Trim();
        if (request.Description is not null) item.Description = request.Description.Trim();
        if (request.Amount is not null) item.Amount = Math.Round(request.Amount.Value, 2, MidpointRounding.AwayFromZero);
        if (request.IssueDate is not null) item.IssueDate = request.IssueDate.Value;
        if (request.Category is not null) item.Category = request.Category;
        if (request.PaidDate is not null)
        {
            if (item.Status != "pago") throw new ArgumentException("Só é possível mudar a data de pagamento de uma conta já paga.");
            if (request.PaidDate.Value > DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3))) throw new ArgumentException("A data de pagamento não pode ser no futuro.");
            item.PaidDate = request.PaidDate.Value;
            item.DueLabel = $"Pago em {item.PaidDate:dd/MM/yyyy}";
        }
        if (request.DueDate is not null && item.Status != "pago")
        {
            item.DueDate = request.DueDate.Value;
            item.DueLabel = DateLabelHelper.DueLabelFor(item.DueDate);
            item.Status = item.DueDate < DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3)) ? "vencido" : "a_vencer";
        }

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("editou", "Conta a Pagar", $"{item.Supplier} • {item.Description}", null, ct);
        return ToDto(item);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var item = await db.AccountsPayables.FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == currentUser.CompanyId, ct);
        if (item is null) return false;
        if (item.Category == "compras" || item.PurchaseEntryId.HasValue)
        {
            throw new InvalidOperationException("Esta conta veio de uma compra: edite ou exclua a compra em Compras / Entradas.");
        }

        db.AccountsPayables.Remove(item);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("excluiu", "Conta a Pagar", $"{item.Description} • {item.Amount:0.00}", item.Status, ct);
        return true;
    }

    public async Task<AccountsPayableDto?> ChangeCategoryAsync(Guid id, ChangePayableCategoryRequest request, CancellationToken ct)
    {
        if (request.Category is not ("equipamento" or "outros"))
        {
            throw new ArgumentException("Categoria inválida: use 'equipamento' ou 'outros'.");
        }

        var item = await db.AccountsPayables.FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == currentUser.CompanyId, ct);
        if (item is null) return null;
        if (item.Category == "compras" || item.PurchaseEntryId.HasValue)
        {
            throw new ArgumentException("Compras de mercadoria não mudam de categoria.");
        }

        item.Category = request.Category;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("reclassificou", "Conta a Pagar", $"{item.Description} → {request.Category}", null, ct);
        return ToDto(item);
    }

    public async Task<AccountsPayableDto?> SettleAsync(Guid id, SettlePayableRequest request, CancellationToken ct)
    {
        var item = await db.AccountsPayables.FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == currentUser.CompanyId, ct);
        if (item is null) return null;

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
        item.Status = "pago";
        item.PaidDate = today;
        item.DueLabel = $"Pago em {today:dd/MM/yyyy}";
        item.ReceiptAttached = true;

        if (item.PurchaseEntryId.HasValue)
        {
            var purchase = await db.PurchaseEntries
                .FirstOrDefaultAsync(p => p.Id == item.PurchaseEntryId && p.CompanyId == currentUser.CompanyId, ct);
            if (purchase is not null) purchase.PaymentStatus = "pago";
        }

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("baixou", "Conta a Pagar", $"{item.Supplier} • {item.Description}", null, ct);
        return ToDto(item);
    }

    private static AccountsPayableDto ToDto(AccountsPayable p) => new(
        p.Id, p.Supplier, p.Description, p.NfeReference, p.IssueDate, p.DueDate, p.DueLabel, p.Amount,
        p.PaymentMethod, p.Origin, p.Status, p.PaidDate, p.ReceiptAttached, p.Category);
}
