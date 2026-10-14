namespace GCStock.Application.Storage;

public record StoredFile(string Key, string Url, string ContentType, long Size);

/// <summary>URL pré-assinada para o cliente enviar o arquivo direto ao storage (PUT), sem passar pela API.</summary>
public record PresignedUpload(string UploadUrl, string Key, string Url, string ContentType, DateTime ExpiresAtUtc);

public interface IFileStorage
{
    /// <summary>false quando as credenciais do storage não foram configuradas (a API sobe normalmente, mas uploads respondem 503).</summary>
    bool IsConfigured { get; }

    Task<StoredFile> UploadAsync(Stream content, string key, string contentType, long size, CancellationToken ct);

    PresignedUpload CreatePresignedUpload(string key, string contentType, TimeSpan expiresIn);

    Task DeleteAsync(string key, CancellationToken ct);
}
