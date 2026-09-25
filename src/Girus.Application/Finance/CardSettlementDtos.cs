namespace Girus.Application.Finance;

public record CardSettlementDto(
    Guid Id,
    string Acquirer,
    string CardBrand,
    DateOnly SaleDate,
    string SettlementDate,
    decimal GrossAmount,
    decimal MdrFeeAmount,
    decimal NetAmount,
    string Status,
    decimal? AnticipatedFee);

public interface ICardSettlementService
{
    Task<List<CardSettlementDto>> GetAllAsync(CancellationToken ct);

    /// <summary>Antecipa o recebível cobrando 2,5% de taxa sobre o valor bruto.</summary>
    Task AnticipateAsync(Guid id, CancellationToken ct);
}
