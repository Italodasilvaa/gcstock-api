using GCStock.Application.Auth;

namespace GCStock.Application.Tenants;

public record BranchDto(
    Guid Id,
    string Name,
    string Location,
    string Cnpj,
    int OperatorsCount,
    bool Active,
    string Type);

public record UpdateCompanyRequest(
    string CorporateName,
    string? TradeName,
    string Document,
    string? Phone,
    string? Segment,
    string? City,
    string? State);

public interface ITenantService
{
    /// <summary>Empresas do mesmo Administrator (dono) da sessão ativa — usado no seletor de empresas e na transferência de funcionários.</summary>
    Task<List<CompanySummaryDto>> GetMyCompaniesAsync(CancellationToken ct);

    /// <summary>Troca a empresa ativa. Só é permitida entre empresas do mesmo Administrator, e só para o próprio dono
    /// (funcionário não troca de empresa). Retorna null quando a troca não é permitida.</summary>
    Task<AuthSessionDto?> SwitchCompanyAsync(Guid companyId, CancellationToken ct);

    Task<List<BranchDto>> GetBranchesAsync(CancellationToken ct);

    /// <summary>Altera os dados cadastrais da empresa ativa (dono ou funcionário Administrador). Retorna null quando quem chama não tem acesso.
    /// Lança ArgumentException quando um campo obrigatório vem vazio.</summary>
    Task<CompanySummaryDto?> UpdateCurrentCompanyAsync(UpdateCompanyRequest request, CancellationToken ct);
}
