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
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Method)
            .Select(f => ToDto(f))
            .ToListAsync(ct);
    }

    public async Task<PaymentFeeSettingDto> CreateAsync(CreateFeeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Method) || string.IsNullOrWhiteSpace(request.Channel))
            throw new InvalidOperationException("Informe o método e o canal/adquirente da taxa.");

        var fee = new Domain.Entities.PaymentFeeSetting
        {
            CompanyId = currentUser.CompanyId,
            Method = request.Method.Trim(),
            Subtitle = request.Subtitle?.Trim() ?? string.Empty,
            Channel = request.Channel.Trim(),
            FeePercent = request.FeePercent,
            FixedFee = request.FixedFee,
            SettlementTerm = request.SettlementTerm?.Trim() ?? string.Empty,
            Active = request.Active,
            Icon = string.IsNullOrWhiteSpace(request.Icon) ? "credit_card" : request.Icon.Trim(),
            SortOrder = (await db.PaymentFeeSettings
                .Where(f => f.CompanyId == currentUser.CompanyId)
                .MaxAsync(f => (int?)f.SortOrder, ct) ?? -1) + 1,
        };

        db.PaymentFeeSettings.Add(fee);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("criou", "Taxa de Pagamento", fee.Method, null, ct);
        return ToDto(fee);
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

    public async Task<List<PaymentFeeSettingDto>> ReorderAsync(ReorderFeesRequest request, CancellationToken ct)
    {
        var fees = await db.PaymentFeeSettings
            .Where(f => f.CompanyId == currentUser.CompanyId)
            .ToListAsync(ct);

        var position = 0;
        foreach (var id in request.Ids)
        {
            var fee = fees.FirstOrDefault(f => f.Id == id);
            if (fee is not null) fee.SortOrder = position++;
        }

        foreach (var fee in fees.Where(f => !request.Ids.Contains(f.Id)).OrderBy(f => f.SortOrder))
            fee.SortOrder = position++;

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("editou", "Taxa de Pagamento", "Ordem das formas de pagamento", null, ct);
        return await GetAllAsync(ct);
    }

    private static PaymentFeeSettingDto ToDto(Domain.Entities.PaymentFeeSetting f) => new(
        f.Id, f.Method, f.Subtitle, f.Channel, f.FeePercent, f.FixedFee, f.SettlementTerm, f.Active, f.Icon, f.SortOrder);
}
