using Girus.Application.Audit;
using Girus.Application.Abstractions;
using Girus.Application.Finance;
using Girus.Domain.Entities;
using Girus.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace Girus.Infra.Services;

public class AlertSettingService(GirusDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit) : IAlertSettingService
{
    public async Task<GlobalAlertSettingDto> GetAsync(CancellationToken ct)
    {
        var settings = await db.AlertSettings.FirstOrDefaultAsync(a => a.CompanyId == currentUser.CompanyId, ct);
        settings ??= new AlertSetting { CompanyId = currentUser.CompanyId };
        return ToDto(settings);
    }

    public async Task UpdateAsync(GlobalAlertSettingDto request, CancellationToken ct)
    {
        var settings = await db.AlertSettings.FirstOrDefaultAsync(a => a.CompanyId == currentUser.CompanyId, ct);
        if (settings is null)
        {
            settings = new AlertSetting { CompanyId = currentUser.CompanyId };
            db.AlertSettings.Add(settings);
        }

        settings.AlertLeadDays = request.AlertLeadDays;
        settings.MorningSummaryTime = request.MorningSummaryTime;
        settings.NotifyTopbar = request.NotifyTopbar;
        settings.NotifyEmail = request.NotifyEmail;
        settings.NotifyWhatsapp = request.NotifyWhatsapp;
        settings.Recipients = request.Recipients;

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("editou", "Alertas Sanitários", "Configuração de alertas de validade", null, ct);
    }

    private static GlobalAlertSettingDto ToDto(AlertSetting a) => new(
        a.AlertLeadDays, a.MorningSummaryTime, a.NotifyTopbar, a.NotifyEmail, a.NotifyWhatsapp, a.Recipients);
}
