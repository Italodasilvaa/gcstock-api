namespace GCStock.Application.Finance;

public record GlobalAlertSettingDto(
    int AlertLeadDays,
    string MorningSummaryTime,
    bool NotifyTopbar,
    bool NotifyEmail,
    bool NotifyWhatsapp,
    List<string> Recipients,
    /// <summary>Margem mínima padrão (%) dos kits da empresa; cada kit pode ter a sua.</summary>
    decimal MinKitMarginPercent = 30m,
    /// <summary>Meta mensal de faturamento da empresa (R$).</summary>
    decimal MonthlyRevenueGoal = 3000m);

public interface IAlertSettingService
{
    Task<GlobalAlertSettingDto> GetAsync(CancellationToken ct);
    Task UpdateAsync(GlobalAlertSettingDto request, CancellationToken ct);
}
