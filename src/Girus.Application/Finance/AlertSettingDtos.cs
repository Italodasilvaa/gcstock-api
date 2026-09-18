namespace Girus.Application.Finance;

public record GlobalAlertSettingDto(
    int AlertLeadDays,
    string MorningSummaryTime,
    bool NotifyTopbar,
    bool NotifyEmail,
    bool NotifyWhatsapp,
    List<string> Recipients);

public interface IAlertSettingService
{
    Task<GlobalAlertSettingDto> GetAsync(CancellationToken ct);
    Task UpdateAsync(GlobalAlertSettingDto request, CancellationToken ct);
}
