namespace Girus.Application.Clients;

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
}
