using Girus.Application.Audit;
using Girus.Application.Abstractions;
using Girus.Application.Finance;
using Girus.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace Girus.Infra.Services;

public class FeeService(GirusDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit) : IFeeService
{
    public async Task<List<PaymentFeeSettingDto>> GetAllAsync(CancellationToken ct)
    {
        return await db.PaymentFeeSettings
            .Where(f => f.CompanyId == currentUser.CompanyId)
            .Select(f => ToDto(f))
            .ToListAsync(ct);
    }

    public async Task<PaymentFeeSettingDto> SaveAsync(SaveFeeRequest request, CancellationToken ct)
    {
        var fee = await db.PaymentFeeSettings.FirstOrDefaultAsync(f => f.Id == request.Id && f.CompanyId == currentUser.CompanyId, ct)
            ?? throw new KeyNotFoundException($"Taxa '{request.Id}' não encontrada.");

        if (request.Method is not null) fee.Method = request.Method;
        if (request.Subtitle is not null) fee.Subtitle = request.Subtitle;
        if (request.Channel is not null) fee.Channel = request.Channel;
        if (request.FeePercent is not null) fee.FeePercent = request.FeePercent.Value;
        if (request.FixedFee is not null) fee.FixedFee = request.FixedFee;
        if (request.SettlementTerm is not null) fee.SettlementTerm = request.SettlementTerm;
        if (request.Active is not null) fee.Active = request.Active.Value;
        if (request.Icon is not null) fee.Icon = request.Icon;

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("editou", "Taxa de Pagamento", fee.Method, null, ct);
        return ToDto(fee);
    }

    private static PaymentFeeSettingDto ToDto(Domain.Entities.PaymentFeeSetting f) => new(
        f.Id, f.Method, f.Subtitle, f.Channel, f.FeePercent, f.FixedFee, f.SettlementTerm, f.Active, f.Icon);
}
