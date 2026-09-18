using Girus.Application.Auth;

namespace Girus.Application.Tenants;

public record BranchDto(
    string Id,
    string Name,
    string Location,
    string Cnpj,
    int OperatorsCount,
    bool Active,
    string Type);

public interface ITenantService
{
    /// <summary>Empresas do mesmo Administrator (dono) da sessão ativa — usado no seletor de empresas e na transferência de funcionários.</summary>
    Task<List<CompanySummaryDto>> GetMyCompaniesAsync(CancellationToken ct);

    /// <summary>Troca a empresa ativa. Só é permitida entre empresas do mesmo Administrator, e só para o próprio dono
    /// (funcionário não troca de empresa). Retorna null quando a troca não é permitida.</summary>
    Task<AuthSessionDto?> SwitchCompanyAsync(string companyId, CancellationToken ct);

    Task<List<BranchDto>> GetBranchesAsync(CancellationToken ct);
}
