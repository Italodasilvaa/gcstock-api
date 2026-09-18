namespace Girus.Application.Finance;

public record CardSettlementDto(
    string Id,
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
    Task AnticipateAsync(string id, CancellationToken ct);
}
