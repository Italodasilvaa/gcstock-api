using GCStock.Application.Audit;
using GCStock.Application.Abstractions;
using GCStock.Application.Common;
using GCStock.Application.Finance;
using GCStock.Domain.Entities;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

public class ReceivableService(GCStockDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit) : IReceivableService
{
    public async Task<List<AccountsReceivableDto>> GetAllAsync(CancellationToken ct)
    {
        return await db.AccountsReceivables
            .Where(r => r.CompanyId == currentUser.CompanyId)
            .OrderBy(r => r.DueDate)
            .Select(r => ToDto(r))
            .ToListAsync(ct);
    }

    public async Task<AccountsReceivableDto> AddAsync(AddReceivableRequest request, CancellationToken ct)
    {
        var gross = request.GrossAmount ?? 0;
        var fee = request.FeeRetention ?? 0;
        var dueDate = request.DueDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3).AddDays(20));

        var saved = new AccountsReceivable
        {
            CompanyId = currentUser.CompanyId,
            ClientOrOrigin = request.ClientOrOrigin ?? "Cliente Avulso",
            SubCategory = request.SubCategory ?? "Venda a Prazo",
            Reference = request.Reference ?? "Ped #" + Random.Shared.Next(1000, 10000),
            DueDate = dueDate,
            DueLabel = request.DueLabel ?? DateLabelHelper.DueLabelFor(dueDate),
            GrossAmount = gross,
            FeeRetention = fee,
            NetAmount = gross - fee,
            Status = request.Status ?? "a_vencer",
            Type = request.Type ?? "b2b",
            PaymentModeDesc = request.PaymentModeDesc ?? "Boleto / Pix",
        };
        db.AccountsReceivables.Add(saved);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("criou", "Conta a Receber", saved.ClientOrOrigin, null, ct);
        return ToDto(saved);
    }

    private static bool IsFromSale(AccountsReceivable r) => r.Reference.StartsWith("NFCe-", StringComparison.OrdinalIgnoreCase);

    public async Task<AccountsReceivableDto?> UpdateAsync(Guid id, UpdateReceivableRequest request, CancellationToken ct)
    {
        var item = await db.AccountsReceivables.FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == currentUser.CompanyId, ct);
        if (item is null) return null;
        if (item.Status == "recebido") throw new InvalidOperationException("Título já recebido não pode ser editado.");
        if (request.GrossAmount is <= 0) throw new ArgumentException("O valor deve ser maior que zero.");

        if (request.DueDate is not null)
        {
            item.DueDate = request.DueDate.Value;
            item.DueLabel = DateLabelHelper.DueLabelFor(item.DueDate);
            item.Status = item.DueDate < DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3)) ? "vencido" : "a_vencer";
        }

        if (!IsFromSale(item))
        {
            if (request.ClientOrOrigin is not null) item.ClientOrOrigin = request.ClientOrOrigin.Trim();
            if (request.SubCategory is not null) item.SubCategory = request.SubCategory.Trim();
            if (request.GrossAmount is not null)
            {
                item.GrossAmount = Math.Round(request.GrossAmount.Value, 2, MidpointRounding.AwayFromZero);
                item.NetAmount = item.GrossAmount - item.FeeRetention;
            }
        }

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("editou", "Conta a Receber", item.ClientOrOrigin, null, ct);
        return ToDto(item);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var item = await db.AccountsReceivables.FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == currentUser.CompanyId, ct);
        if (item is null) return false;
        if (item.Status == "recebido") throw new InvalidOperationException("Título já recebido não pode ser excluído.");
        if (IsFromSale(item)) throw new InvalidOperationException("Este título veio de uma venda: cancele a venda em Histórico de Vendas.");

        db.AccountsReceivables.Remove(item);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("excluiu", "Conta a Receber", item.ClientOrOrigin, null, ct);
        return true;
    }

    public async Task<AccountsReceivableDto?> SettleAsync(Guid id, CancellationToken ct)
    {
        var item = await db.AccountsReceivables.FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == currentUser.CompanyId, ct);
        if (item is null) return null;

        item.Status = "recebido";
        item.DueLabel = "Recebido / Conciliado";
        item.ReceivedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("recebeu", "Conta a Receber", item.ClientOrOrigin, null, ct);
        return ToDto(item);
    }

    private static AccountsReceivableDto ToDto(AccountsReceivable r) => new(
        r.Id, r.ClientOrOrigin, r.SubCategory, r.Reference, r.DueDate, r.DueLabel, r.GrossAmount, r.FeeRetention,
        r.NetAmount, r.Status, r.Type, r.PaymentModeDesc, r.ReceivedDate);
}
