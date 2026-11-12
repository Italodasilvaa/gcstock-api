using System.Collections.Concurrent;

namespace GCStock.Infra.Security;

/// <summary>
/// Bloqueio temporário por e-mail após falhas seguidas de login (força bruta). Fica em memória, por instância da API:
/// reiniciar a API libera os bloqueios, o que é aceitável para frear tentativas automatizadas.
/// </summary>
public class LoginAttemptTracker(TimeProvider clock)
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, (int Failures, DateTimeOffset? LockedUntil)> _attempts = new();

    public bool IsLocked(string email)
    {
        if (!_attempts.TryGetValue(Key(email), out var state) || state.LockedUntil is null) return false;
        if (state.LockedUntil > clock.GetUtcNow()) return true;

        // Bloqueio expirou: recomeça a contagem.
        _attempts.TryRemove(Key(email), out _);
        return false;
    }

    public void RegisterFailure(string email)
    {
        _attempts.AddOrUpdate(
            Key(email),
            _ => (1, null),
            (_, state) =>
            {
                var failures = state.Failures + 1;
                return failures >= MaxFailures ? (failures, clock.GetUtcNow().Add(LockDuration)) : (failures, null);
            });
    }

    public void RegisterSuccess(string email) => _attempts.TryRemove(Key(email), out _);

    private static string Key(string email) => email.Trim().ToLowerInvariant();
}

/// <summary>Login bloqueado temporariamente por excesso de tentativas.</summary>
public class LoginLockedException() : Exception("Muitas tentativas de login. Tente novamente em alguns minutos.");
