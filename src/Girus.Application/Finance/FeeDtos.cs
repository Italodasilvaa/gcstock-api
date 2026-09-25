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
    string Icon,
    int SortOrder);

public record ReorderFeesRequest(List<string> Ids);

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

public record CreateFeeRequest(
    string Method,
    string? Subtitle,
    string Channel,
    decimal FeePercent,
    decimal? FixedFee,
    string? SettlementTerm,
    bool Active,
    string? Icon);

public interface IFeeService
{
    Task<List<PaymentFeeSettingDto>> GetAllAsync(CancellationToken ct);
    Task<PaymentFeeSettingDto> CreateAsync(CreateFeeRequest request, CancellationToken ct);
    Task<PaymentFeeSettingDto> SaveAsync(SaveFeeRequest request, CancellationToken ct);
    /// <summary>Define a ordem das taxas; ids ausentes vão para o fim mantendo a ordem relativa.</summary>
    Task<List<PaymentFeeSettingDto>> ReorderAsync(ReorderFeesRequest request, CancellationToken ct);
}
