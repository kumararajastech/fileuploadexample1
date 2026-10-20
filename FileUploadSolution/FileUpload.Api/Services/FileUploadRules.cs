using System.Text;
using FileUpload.Api.Options;
using Microsoft.AspNetCore.Http;

namespace FileUpload.Api.Services;

/// <summary>
/// The result of validating an upload request before storage is contacted.
/// </summary>
/// <param name="StatusCode">The HTTP status to return when validation fails.</param>
/// <param name="ErrorCode">A stable error code.</param>
/// <param name="Detail">A client-safe error detail.</param>
/// <param name="Extension">The validated lowercase extension, including the leading dot.</param>
/// <param name="DisplayName">A sanitized name suitable for the API response.</param>
internal readonly record struct UploadValidationResult(
    int? StatusCode,
    string? ErrorCode,
    string? Detail,
    string? Extension,
    string? DisplayName)
{
    public bool Succeeded => StatusCode is null;

    public static UploadValidationResult Success(string extension, string displayName) =>
        new(null, null, null, extension, displayName);

    public static UploadValidationResult Fail(int statusCode, string errorCode, string detail) =>
        new(statusCode, errorCode, detail, null, null);
}

/// <summary>
/// Validates untrusted upload metadata. File names are never used as storage paths.
/// </summary>
internal static class FileUploadRules
{
    private static readonly HashSet<string> DeniedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".com", ".scr", ".ps1", ".vbs", ".js", ".mjs",
        ".hta", ".msi", ".jar", ".aspx", ".ashx", ".asmx", ".reg", ".lnk", ".pif", ".wsf", ".cpl"
    };

    public static UploadValidationResult Evaluate(IFormFile? file, FileStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (file is null)
        {
            return UploadValidationResult.Fail(StatusCodes.Status400BadRequest, "file_required", "A file is required.");
        }

        if (file.Length < 0)
        {
            return UploadValidationResult.Fail(StatusCodes.Status400BadRequest, "file_invalid", "The file is invalid.");
        }

        if (file.Length == 0)
        {
            return UploadValidationResult.Fail(StatusCodes.Status400BadRequest, "file_empty", "The file is empty.");
        }

        if (file.Length > options.MaxFileSizeInBytes)
        {
            return UploadValidationResult.Fail(
                StatusCodes.Status413PayloadTooLarge,
                "file_too_large",
                $"The file exceeds the maximum allowed size of {options.MaxFileSizeInMB} MB.");
        }

        if (!TryGetAllowedExtension(file.FileName, options.AllowedExtensions, out var extension))
        {
            return UploadValidationResult.Fail(
                StatusCodes.Status400BadRequest,
                "file_extension_not_allowed",
                "The file extension is not allowed.");
        }

        return UploadValidationResult.Success(extension, SanitizeDisplayName(file.FileName, extension));
    }

    private static bool TryGetAllowedExtension(
        string? clientFileName,
        IReadOnlyCollection<string>? allowedExtensions,
        out string extension)
    {
        extension = string.Empty;

        if (string.IsNullOrWhiteSpace(clientFileName) ||
            clientFileName.Contains('\0') ||
            allowedExtensions is null ||
            allowedExtensions.Count == 0)
        {
            return false;
        }

        var originalName = Path.GetFileName(clientFileName);
        if (ContainsDeniedSegment(originalName))
        {
            return false;
        }

        var candidate = Path.GetExtension(originalName);
        if (string.IsNullOrEmpty(candidate) || candidate.IndexOfAny(['/', '\\', ':']) >= 0)
        {
            return false;
        }

        foreach (var allowed in allowedExtensions)
        {
            if (string.Equals(allowed, candidate, StringComparison.OrdinalIgnoreCase))
            {
                extension = candidate.ToLowerInvariant();
                return true;
            }
        }

        return false;
    }

    private static bool ContainsDeniedSegment(string fileName)
    {
        var segments = fileName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments.Skip(1))
        {
            if (DeniedExtensions.Contains("." + segment))
            {
                return true;
            }
        }

        return false;
    }

    private static string SanitizeDisplayName(string? clientFileName, string extension)
    {
        var fileName = Path.GetFileName(clientFileName ?? string.Empty);
        var builder = new StringBuilder(fileName.Length);
        foreach (var character in fileName)
        {
            if (!char.IsControl(character) && character is not ('<' or '>' or ':' or '"' or '|' or '?' or '*' or '\\' or '/'))
            {
                builder.Append(character);
            }
        }

        var sanitized = builder.ToString().Trim();
        if (sanitized.Length > 180)
        {
            sanitized = sanitized[..180];
        }

        if (string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or "..")
        {
            return "upload" + extension;
        }

        return sanitized;
    }
}
