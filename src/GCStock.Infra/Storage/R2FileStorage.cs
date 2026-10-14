using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using GCStock.Application.Storage;
using Microsoft.Extensions.Options;

namespace GCStock.Infra.Storage;

/// <summary>Armazena arquivos no Cloudflare R2 (API compatível com S3).</summary>
public sealed class R2FileStorage(IOptions<R2Settings> options) : IFileStorage, IDisposable
{
    private readonly R2Settings _settings = options.Value;
    private readonly Lazy<AmazonS3Client> _client = new(() => CreateClient(options.Value));

    public bool IsConfigured => _settings.IsConfigured;

    public async Task<StoredFile> UploadAsync(Stream content, string key, string contentType, long size, CancellationToken ct)
    {
        EnsureConfigured();

        var request = new PutObjectRequest
        {
            BucketName = _settings.Bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            // O R2 não aceita assinatura de payload em streaming; sobre HTTPS a assinatura do payload é dispensável.
            DisablePayloadSigning = true,
            UseChunkEncoding = false,
        };
        request.Headers.ContentLength = size;

        await _client.Value.PutObjectAsync(request, ct);
        return new StoredFile(key, PublicUrl(key), contentType, size);
    }

    public PresignedUpload CreatePresignedUpload(string key, string contentType, TimeSpan expiresIn)
    {
        EnsureConfigured();

        var expires = DateTime.UtcNow.Add(expiresIn);
        var url = _client.Value.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _settings.Bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = expires,
            ContentType = contentType,
        });
        return new PresignedUpload(url, key, PublicUrl(key), contentType, expires);
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        EnsureConfigured();
        await _client.Value.DeleteObjectAsync(_settings.Bucket, key, ct);
    }

    public void Dispose()
    {
        if (_client.IsValueCreated) _client.Value.Dispose();
    }

    private string PublicUrl(string key) => $"{_settings.PublicBaseUrl.TrimEnd('/')}/{key}";

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured)
        {
            throw new InvalidOperationException(
                "Storage R2 não configurado (Storage:R2:AccountId, AccessKeyId, SecretAccessKey, Bucket, PublicBaseUrl).");
        }
    }

    private static AmazonS3Client CreateClient(R2Settings settings)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = $"https://{settings.AccountId}.r2.cloudflarestorage.com",
            ForcePathStyle = true,
            AuthenticationRegion = "auto",
            // O R2 não suporta os checksums adicionais (CRC) que o SDK novo envia por padrão.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        return new AmazonS3Client(new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey), config);
    }
}
