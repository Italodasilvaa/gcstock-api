using System.Text.RegularExpressions;

namespace GCStock.Application.Storage;

/// <summary>Regras de validação e de nomeação (chave) dos arquivos enviados ao storage.</summary>
public static partial class FileRules
{
    public const long MaxImageBytes = 10L * 1024 * 1024;
    public const long MaxDocumentBytes = 25L * 1024 * 1024;
    public const long MaxVideoBytes = 500L * 1024 * 1024;

    private static readonly Dictionary<string, string[]> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = [".jpg", ".jpeg"],
        ["image/png"] = [".png"],
        ["image/webp"] = [".webp"],
        ["image/gif"] = [".gif"],
        ["image/avif"] = [".avif"],
        ["video/mp4"] = [".mp4"],
        ["video/webm"] = [".webm"],
        ["video/quicktime"] = [".mov"],
        ["application/pdf"] = [".pdf"],
        ["text/csv"] = [".csv"],
        ["text/plain"] = [".txt"],
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = [".xlsx"],
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = [".docx"],
        ["application/zip"] = [".zip"],
    };

    /// <summary>Valida tipo, extensão e tamanho. Retorna a extensão normalizada em minúsculas.</summary>
    public static bool TryValidate(string? fileName, string? contentType, long size, out string extension, out string error)
    {
        extension = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(contentType))
        {
            error = "Arquivo ou tipo de conteúdo não informado.";
            return false;
        }

        if (size <= 0)
        {
            error = "Arquivo vazio.";
            return false;
        }

        contentType = contentType.Split(';')[0].Trim();
        if (!AllowedTypes.TryGetValue(contentType, out var extensions))
        {
            error = $"Tipo de arquivo não permitido: {contentType}.";
            return false;
        }

        extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!extensions.Contains(extension))
        {
            error = $"A extensão '{extension}' não corresponde ao tipo {contentType}.";
            return false;
        }

        var max = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? MaxImageBytes
            : contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ? MaxVideoBytes
            : MaxDocumentBytes;
        if (size > max)
        {
            error = $"Arquivo excede o limite de {max / (1024 * 1024)} MB para este tipo.";
            return false;
        }

        return true;
    }

    /// <summary>Chave no bucket: {empresa}/{pasta}/{guid}{ext}. O prefixo da empresa isola os arquivos entre tenants.</summary>
    public static string BuildKey(Guid companyId, string? folder, string extension) =>
        $"{companyId}/{SanitizeFolder(folder)}/{Guid.NewGuid():N}{extension}";

    public static bool BelongsToCompany(string? key, Guid companyId) =>
        !string.IsNullOrWhiteSpace(key)
        && !key.Contains("..", StringComparison.Ordinal)
        && key.StartsWith($"{companyId}/", StringComparison.Ordinal);

    private static string SanitizeFolder(string? folder)
    {
        var value = (folder ?? string.Empty).Trim().ToLowerInvariant();
        return FolderRegex().IsMatch(value) ? value : "geral";
    }

    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex FolderRegex();
}
