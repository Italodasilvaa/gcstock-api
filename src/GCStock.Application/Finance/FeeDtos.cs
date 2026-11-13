namespace GCStock.Application.Finance;

public record PaymentFeeSettingDto(
    Guid Id,
    string Method,
    string Subtitle,
    string Channel,
    decimal FeePercent,
    decimal? FixedFee,
    string SettlementTerm,
    bool Active,
    string Icon,
    int SortOrder,
    /// <summary>Taxa (%) para antecipar recebíveis desta modalidade, sobre o valor líquido da parcela.</summary>
    decimal AnticipationFeePercent = 2.5m);

public record ReorderFeesRequest(List<Guid> Ids);

public record SaveFeeRequest(
    Guid Id,
    string? Method,
    string? Subtitle,
    string? Channel,
    decimal? FeePercent,
    decimal? FixedFee,
    string? SettlementTerm,
    bool? Active,
    string? Icon,
    decimal? AnticipationFeePercent = null);

public record CreateFeeRequest(
    string Method,
    string? Subtitle,
    string Channel,
    decimal FeePercent,
    decimal? FixedFee,
    string? SettlementTerm,
    bool Active,
    string? Icon,
    decimal AnticipationFeePercent = 2.5m);

public interface IFeeService
{
    Task<List<PaymentFeeSettingDto>> GetAllAsync(CancellationToken ct);
    Task<PaymentFeeSettingDto> CreateAsync(CreateFeeRequest request, CancellationToken ct);
    Task<PaymentFeeSettingDto> SaveAsync(SaveFeeRequest request, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
    /// <summary>Define a ordem das taxas; ids ausentes vão para o fim mantendo a ordem relativa.</summary>
    Task<List<PaymentFeeSettingDto>> ReorderAsync(ReorderFeesRequest request, CancellationToken ct);
}
