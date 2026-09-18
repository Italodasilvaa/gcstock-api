using Girus.Application.Audit;
using Girus.Application.Abstractions;
using Girus.Application.Common;
using Girus.Application.Finance;
using Girus.Domain.Entities;
using Girus.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace Girus.Infra.Services;

public class ReceivableService(GirusDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit) : IReceivableService
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
        var dueDate = request.DueDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20));

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

    public async Task<AccountsReceivableDto?> SettleAsync(string id, CancellationToken ct)
    {
        var item = await db.AccountsReceivables.FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == currentUser.CompanyId, ct);
        if (item is null) return null;

        item.Status = "recebido";
        item.DueLabel = "Recebido / Conciliado";
        item.ReceivedDate = DateOnly.FromDateTime(DateTime.UtcNow);

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("recebeu", "Conta a Receber", item.ClientOrOrigin, null, ct);
        return ToDto(item);
    }

    private static AccountsReceivableDto ToDto(AccountsReceivable r) => new(
        r.Id, r.ClientOrOrigin, r.SubCategory, r.Reference, r.DueDate, r.DueLabel, r.GrossAmount, r.FeeRetention,
        r.NetAmount, r.Status, r.Type, r.PaymentModeDesc, r.ReceivedDate);
}
