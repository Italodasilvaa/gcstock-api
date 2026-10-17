namespace GCStock.Application.Clients;

public record ClientDto(
    Guid Id,
    string Name,
    string Document,
    string Phone,
    string? Email,
    string Type,
    int FidelityPoints,
    decimal CreditLimit,
    decimal TotalSpent,
    string FavoriteCuts,
    string? Notes);

public record SaveClientRequest(
    Guid? Id,
    string? Name,
    string? Document,
    string? Phone,
    string? Email,
    string? Type,
    int? FidelityPoints,
    decimal? CreditLimit,
    decimal? TotalSpent,
    string? FavoriteCuts,
    string? Notes);

public interface IClientService
{
    Task<List<ClientDto>> GetAllAsync(CancellationToken ct);
    Task<ClientDto> SaveAsync(SaveClientRequest request, CancellationToken ct);
    /// <summary>Apaga o cliente; recusa (InvalidOperationException) se houver conta a receber em aberto dele.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
}
