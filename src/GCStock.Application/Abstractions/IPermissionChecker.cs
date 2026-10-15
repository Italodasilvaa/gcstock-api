namespace GCStock.Application.Abstractions;

/// <summary>Consulta as permissões do usuário autenticado. O dono da empresa tem todas; funcionários, as cadastradas.</summary>
public interface IPermissionChecker
{
    /// <summary>Lê do banco a cada chamada (não do token), então revogar uma permissão vale imediatamente.</summary>
    Task<bool> HasAsync(string permission, CancellationToken ct);

    Task<List<string>> GetCurrentAsync(CancellationToken ct);
}
