namespace GCStock.Infra.Storage;

/// <summary>Configuração do Cloudflare R2 (seção "Storage:R2"). Credenciais só via user-secrets / Swarm secrets.</summary>
public class R2Settings
{
    public const string SectionName = "Storage:R2";

    /// <summary>Account ID da Cloudflare (Dashboard > R2 > Overview).</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Access Key ID do API token do R2 (permissão Object Read & Write no bucket).</summary>
    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;

    public string Bucket { get; set; } = string.Empty;

    /// <summary>Base pública para leitura (domínio customizado do bucket, ex.: https://cdn.iwsolutions.cloud). Sem barra final.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountId)
        && !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey)
        && !string.IsNullOrWhiteSpace(Bucket)
        && !string.IsNullOrWhiteSpace(PublicBaseUrl);
}
