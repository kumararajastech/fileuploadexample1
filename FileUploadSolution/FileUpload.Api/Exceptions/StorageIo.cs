namespace FileUpload.Api.Exceptions;

/// <summary>
/// Maps Windows and .NET I/O failures to storage failure categories without exposing the path to callers.
/// </summary>
internal static class StorageIo
{
    public static FileStorageException Map(Exception exception, string fullPath)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is FileStorageException storageException)
        {
            return storageException;
        }

        if (IsAccessDenied(exception))
        {
            return new FileStorageException(
                FileStorageFailureReason.AccessDenied,
                "Access to the configured storage location was denied.",
                exception);
        }

        if (IsUnc(fullPath) && (IsNetworkUnavailable(exception) || IsPathNotFound(exception)))
        {
            return new FileStorageException(
                FileStorageFailureReason.ShareUnavailable,
                "The configured storage location is unavailable.",
                exception);
        }

        if (IsNetworkUnavailable(exception))
        {
            return new FileStorageException(
                FileStorageFailureReason.ShareUnavailable,
                "The configured storage location is unavailable.",
                exception);
        }

        if (IsPathNotFound(exception))
        {
            return new FileStorageException(
                FileStorageFailureReason.InvalidConfiguration,
                "The configured storage folder does not exist. The application does not create it.",
                exception);
        }

        if (IsFileAlreadyExists(exception))
        {
            return new FileStorageException(
                FileStorageFailureReason.FileAlreadyExists,
                "A file with the generated name already exists.",
                exception);
        }

        return new FileStorageException(
            FileStorageFailureReason.IoFailure,
            "The file could not be written to storage.",
            exception);
    }

    public static bool IsAccessDenied(Exception exception) =>
        exception is UnauthorizedAccessException || Win32Code(exception) is 5 or 1326;

    public static bool IsNetworkUnavailable(Exception exception) =>
        Win32Code(exception) is 53 or 59 or 64 or 67 or 1203 or 1219 or 1231 or 1232 or 2250;

    public static bool IsPathNotFound(Exception exception) =>
        exception is DirectoryNotFoundException or FileNotFoundException ||
        Win32Code(exception) is 2 or 3 or 15 or 21;

    public static bool IsFileAlreadyExists(Exception exception) =>
        Win32Code(exception) is 80 or 183;

    public static bool IsUnc(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal);

    private static int Win32Code(Exception exception) => exception.HResult & 0xFFFF;
}
