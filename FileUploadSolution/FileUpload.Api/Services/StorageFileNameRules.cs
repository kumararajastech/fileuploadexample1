using System.Text.RegularExpressions;
using FileUpload.Api.Exceptions;

namespace FileUpload.Api.Services;

/// <summary>
/// Rejects storage names that were not produced by <see cref="GuidStorageFileNameFactory"/>.
/// </summary>
internal static partial class StorageFileNameRules
{
    public static void EnsureSafe(string storageFileName, IEnumerable<string> allowedExtensions)
    {
        if (string.IsNullOrWhiteSpace(storageFileName) ||
            !string.Equals(storageFileName, Path.GetFileName(storageFileName), StringComparison.Ordinal) ||
            !StorageNamePattern().IsMatch(storageFileName))
        {
            throw new FileStorageException(
                FileStorageFailureReason.InvalidRequest,
                "The storage file name is invalid.");
        }

        var extension = Path.GetExtension(storageFileName);
        if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new FileStorageException(
                FileStorageFailureReason.InvalidRequest,
                "The storage file extension is not allowed.");
        }
    }

    public static bool IsSafeExtension(string extension) =>
        ExtensionPattern().IsMatch(extension);

    public static string CombineUnderRoot(string rootFullPath, string storageFileName)
    {
        var root = rootFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootPrefix = root + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, storageFileName));

        if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new FileStorageException(
                FileStorageFailureReason.InvalidRequest,
                "The storage file name is invalid.");
        }

        return candidate;
    }

    public static string NormalizeRoot(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath) || !Path.IsPathRooted(configuredPath))
        {
            throw new FileStorageException(
                FileStorageFailureReason.InvalidConfiguration,
                "The storage path configuration is invalid.");
        }

        return Path.GetFullPath(configuredPath.Trim());
    }

    [GeneratedRegex(
        @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\.[A-Za-z0-9]{1,10}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex StorageNamePattern();

    [GeneratedRegex(@"^\.[A-Za-z0-9]{1,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExtensionPattern();
}
