namespace GCStock.Application.Finance;

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

    /// <summary>Antecipa o título de adquirente: recebe hoje o líquido menos a taxa de antecipação da modalidade (despesa financeira).</summary>
    Task AnticipateAsync(Guid id, CancellationToken ct);
}
