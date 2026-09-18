namespace Girus.Application.Finance;

public record PaymentFeeSettingDto(
    string Id,
    string Method,
    string Subtitle,
    string Channel,
    decimal FeePercent,
    decimal? FixedFee,
    string SettlementTerm,
    bool Active,
    string Icon);

public record SaveFeeRequest(
    string Id,
    string? Method,
    string? Subtitle,
    string? Channel,
    decimal? FeePercent,
    decimal? FixedFee,
    string? SettlementTerm,
    bool? Active,
    string? Icon);

public interface IFeeService
{
    Task<List<PaymentFeeSettingDto>> GetAllAsync(CancellationToken ct);
    Task<PaymentFeeSettingDto> SaveAsync(SaveFeeRequest request, CancellationToken ct);
}
