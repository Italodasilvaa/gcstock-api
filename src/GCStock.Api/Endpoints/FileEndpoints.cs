using GCStock.Application.Abstractions;
using GCStock.Application.Storage;
using Microsoft.AspNetCore.Mvc;

namespace GCStock.Api.Endpoints;

public record PresignUploadRequest(string FileName, string ContentType, long Size, string? Folder);

public static class FileEndpoints
{
    // Teto do upload multipart pela API (o proxy da Cloudflare já limita a ~100 MB; arquivos maiores usam /url-upload).
    public const long MaxMultipartBytes = 100L * 1024 * 1024;

    public static void MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        var files = app.MapGroup("/api/v1/arquivos").WithTags("Arquivos").RequireAuthorization();

        // Upload simples: o arquivo passa pela API e vai para o R2. Retorna a URL pública para gravar em ImageUrl etc.
        files.MapPost("/", async (
                IFormFile file,
                [FromQuery] string? pasta,
                ICurrentUserContext user,
                IFileStorage storage,
                CancellationToken ct) =>
            {
                if (!storage.IsConfigured) return NotConfigured();

                if (!FileRules.TryValidate(file.FileName, file.ContentType, file.Length, out var extension, out var error))
                {
                    return Results.BadRequest(new { message = error });
                }

                var key = FileRules.BuildKey(user.CompanyId, pasta, extension);
                await using var stream = file.OpenReadStream();
                var stored = await storage.UploadAsync(stream, key, file.ContentType.Split(';')[0].Trim(), file.Length, ct);
                return Results.Created(stored.Url, stored);
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxMultipartBytes))
            .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxMultipartBytes });

        // Upload direto: devolve uma URL pré-assinada (PUT) e o navegador envia o arquivo ao R2 sem passar pela API.
        // Indicado para vídeos e arquivos grandes. O PUT deve repetir o mesmo Content-Type informado aqui.
        files.MapPost("/url-upload", (
            PresignUploadRequest request,
            ICurrentUserContext user,
            IFileStorage storage) =>
        {
            if (!storage.IsConfigured) return NotConfigured();

            if (!FileRules.TryValidate(request.FileName, request.ContentType, request.Size, out var extension, out var error))
            {
                return Results.BadRequest(new { message = error });
            }

            var key = FileRules.BuildKey(user.CompanyId, request.Folder, extension);
            var presigned = storage.CreatePresignedUpload(key, request.ContentType.Split(';')[0].Trim(), TimeSpan.FromMinutes(15));
            return Results.Ok(presigned);
        });

        // Remove um arquivo da própria empresa (a chave precisa começar com o id da empresa do usuário).
        files.MapDelete("/", async ([FromQuery] string key, ICurrentUserContext user, IFileStorage storage, CancellationToken ct) =>
        {
            if (!storage.IsConfigured) return NotConfigured();
            if (!FileRules.BelongsToCompany(key, user.CompanyId)) return Results.NotFound();

            await storage.DeleteAsync(key, ct);
            return Results.NoContent();
        });
    }

    private static IResult NotConfigured() =>
        Results.Json(new { message = "Armazenamento de arquivos não configurado." }, statusCode: StatusCodes.Status503ServiceUnavailable);
}
