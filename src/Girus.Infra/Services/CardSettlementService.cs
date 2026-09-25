using Girus.Application.Audit;
using Girus.Application.Abstractions;
using Girus.Application.Finance;
using Girus.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace Girus.Infra.Services;

public class CardSettlementService(GirusDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit) : ICardSettlementService
{
    public async Task<List<CardSettlementDto>> GetAllAsync(CancellationToken ct)
    {
        return await db.CardSettlements
            .Where(c => c.CompanyId == currentUser.CompanyId)
            .OrderBy(c => c.SaleDate)
            .Select(c => ToDto(c))
            .ToListAsync(ct);
    }

    public async Task AnticipateAsync(Guid id, CancellationToken ct)
    {
        var item = await db.CardSettlements.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == currentUser.CompanyId, ct);
        if (item is null) return;

        var fee = item.GrossAmount * 0.025m; // 2.5% adiantamento
        item.Status = "antecipado";
        item.AnticipatedFee = fee;
        item.NetAmount -= fee;
        item.SettlementDate = "Hoje (Antecipado)";

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("antecipou", "Liquidação de Cartão", id.ToString(), null, ct);
    }

    private static CardSettlementDto ToDto(Domain.Entities.CardSettlement c) => new(
        c.Id, c.Acquirer, c.CardBrand, c.SaleDate, c.SettlementDate, c.GrossAmount, c.MdrFeeAmount, c.NetAmount,
        c.Status, c.AnticipatedFee);
}
