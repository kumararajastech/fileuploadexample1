using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace FileUpload.Api.Options;

/// <summary>
/// Validates file-storage configuration at startup. It checks the shape of the settings and does not
/// create the storage folder or contact a network share.
/// </summary>
public sealed partial class FileStorageOptionsValidator : IValidateOptions<FileStorageOptions>
{
    /// <summary>Minimum accepted value for <see cref="FileStorageOptions.MaxFileSizeInMB"/>.</summary>
    public const int MinimumFileSizeInMb = 1;

    /// <summary>Maximum accepted value for <see cref="FileStorageOptions.MaxFileSizeInMB"/>.</summary>
    public const int MaximumFileSizeInMb = 512;

    /// <summary>Default maximum file size used when a transport limit must be chosen before validation completes.</summary>
    public const int DefaultMaxFileSizeInMb = 20;

    private static readonly HashSet<string> DeniedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".com", ".scr", ".ps1", ".vbs", ".js", ".mjs",
        ".hta", ".msi", ".jar", ".aspx", ".ashx", ".asmx", ".reg", ".lnk", ".pif", ".wsf", ".cpl"
    };

    /// <summary>
    /// Validates a bound <see cref="FileStorageOptions"/> instance.
    /// </summary>
    /// <param name="name">The named options instance, or <see langword="null"/> for the default instance.</param>
    /// <param name="options">The options to validate.</param>
    /// <returns>Success, or a failure that lists every configuration problem.</returns>
    public ValidateOptionsResult Validate(string? name, FileStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (!TryValidateRoot(options.SharedFolderPath, out var pathError))
        {
            failures.Add(pathError);
        }

        if (options.MaxFileSizeInMB is < MinimumFileSizeInMb or > MaximumFileSizeInMb)
        {
            failures.Add(
                $"FileStorage:MaxFileSizeInMB must be between {MinimumFileSizeInMb} and {MaximumFileSizeInMb}.");
        }

        if (options.AllowedExtensions is null || options.AllowedExtensions.Length == 0)
        {
            failures.Add("FileStorage:AllowedExtensions must contain at least one extension.");
        }
        else
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var extension in options.AllowedExtensions)
            {
                if (!TryValidateExtension(extension, out var extensionError))
                {
                    failures.Add(extensionError);
                    continue;
                }

                if (!seen.Add(extension))
                {
                    failures.Add($"FileStorage:AllowedExtensions contains the duplicate extension '{extension}'.");
                }
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool TryValidateRoot(string? path, out string error)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "FileStorage:SharedFolderPath is required and must be an absolute local path or a UNC path.";
            return false;
        }

        var trimmed = path.Trim();
        if (trimmed.IndexOfAny(['\0', '*', '?', '"', '<', '>', '|']) >= 0 ||
            trimmed.Contains("://", StringComparison.Ordinal) ||
            trimmed.Contains('@', StringComparison.Ordinal))
        {
            error = "FileStorage:SharedFolderPath must be a local path or UNC path without embedded credentials.";
            return false;
        }

        if (!Path.IsPathRooted(trimmed))
        {
            error = "FileStorage:SharedFolderPath must be an absolute local path or a UNC path.";
            return false;
        }

        var segments = trimmed.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == ".."))
        {
            error = "FileStorage:SharedFolderPath must not contain parent-directory segments.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateExtension(string? extension, out string error)
    {
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensionPattern().IsMatch(extension))
        {
            error = "FileStorage:AllowedExtensions entries must be a leading dot followed by 1 to 10 letters or digits.";
            return false;
        }

        if (DeniedExtensions.Contains(extension))
        {
            error = $"FileStorage:AllowedExtensions cannot include the executable or script extension '{extension}'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    [GeneratedRegex(@"^\.[A-Za-z0-9]{1,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedExtensionPattern();
}
