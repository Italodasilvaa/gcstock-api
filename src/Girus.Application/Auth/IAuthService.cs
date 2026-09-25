namespace Girus.Application.Auth;

public interface IAuthService
{
    Task<AuthSessionDto> RegisterCompanyAsync(RegisterCompanyRequest request, CancellationToken ct);

    /// <returns>null quando as credenciais são inválidas (e-mail não encontrado, funcionário inativo ou senha incorreta).</returns>
    Task<AuthSessionDto?> LoginAsync(LoginRequest request, CancellationToken ct);

    /// <summary>Reconstrói a sessão atual a partir do token (usado pelo front-end ao inicializar o app).</summary>
    Task<AuthSessionDto?> GetCurrentSessionAsync(CancellationToken ct);

    /// <summary>Emite um novo token para o usuário autenticado, estendendo a sessão sem exigir login de novo.</summary>
    Task<AuthSessionDto?> RenewTokenAsync(CancellationToken ct);

    /// <summary>Lista pública (sem dados sensíveis) para a tela de login exibir as empresas de demonstração.</summary>
    Task<List<CompanySummaryDto>> ListCompaniesForLoginAsync(CancellationToken ct);

    /// <summary>Conveniência de demonstração para o "acesso rápido" da tela de login. Não deve ser usada em produção real.</summary>
    Task<string?> GetCompanyOwnerEmailAsync(string companyId, CancellationToken ct);
}
