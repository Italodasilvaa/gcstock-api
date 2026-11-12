namespace GCStock.Application.Auth;

public interface IAuthService
{
    Task<AuthSessionDto> RegisterCompanyAsync(RegisterCompanyRequest request, CancellationToken ct);

    /// <returns>null quando as credenciais são inválidas (e-mail não encontrado, funcionário inativo ou senha incorreta).</returns>
    Task<AuthSessionDto?> LoginAsync(LoginRequest request, CancellationToken ct);

    /// <summary>Reconstrói a sessão atual a partir do token (usado pelo front-end ao inicializar o app).</summary>
    Task<AuthSessionDto?> GetCurrentSessionAsync(CancellationToken ct);

    /// <summary>Emite um novo token para o usuário autenticado, estendendo a sessão sem exigir login de novo.</summary>
    Task<AuthSessionDto?> RenewTokenAsync(CancellationToken ct);
}
