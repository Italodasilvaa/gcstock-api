namespace Girus.Infra.Security;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "girus-api";
    public string Audience { get; set; } = "girus-ui";

    /// <summary>Chave secreta usada para assinar os tokens (HMAC-SHA256). DEVE ter pelo menos 32 caracteres
    /// e ser sobrescrita via variável de ambiente/user-secrets em produção — nunca reaproveite o valor de desenvolvimento.</summary>
    public string SecretKey { get; set; } = string.Empty;

    public int ExpiresMinutes { get; set; } = 480;
}
