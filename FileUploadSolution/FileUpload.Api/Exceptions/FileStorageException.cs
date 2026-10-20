namespace FileUpload.Api.Exceptions;

/// <summary>
/// The category of a storage failure. These values are for server logs and exception handling.
/// They are not written to API clients as infrastructure diagnostics.
/// </summary>
public enum FileStorageFailureReason
{
    /// <summary>The storage operation was given an invalid file name or empty content.</summary>
    InvalidRequest = 0,

    /// <summary>The configured folder is missing or is not a directory. The application does not create it.</summary>
    InvalidConfiguration = 1,

    /// <summary>The process identity could not access the storage location.</summary>
    AccessDenied = 2,

    /// <summary>The network share or file server could not be reached.</summary>
    ShareUnavailable = 3,

    /// <summary>A file with the generated name already exists. The existing file was left unchanged.</summary>
    FileAlreadyExists = 4,

    /// <summary>The file is larger than the configured maximum.</summary>
    FileTooLarge = 5,

    /// <summary>An unexpected I/O failure occurred.</summary>
    IoFailure = 6
}

/// <summary>
/// Raised when a file cannot be stored. The message is safe to log and must not be returned to API clients.
/// </summary>
public sealed class FileStorageException : Exception
{
    /// <summary>
    /// Creates a storage exception.
    /// </summary>
    /// <param name="reason">The failure category.</param>
    /// <param name="message">A diagnostic message that does not include file contents or credentials.</param>
    /// <param name="innerException">The underlying framework exception, if any.</param>
    public FileStorageException(FileStorageFailureReason reason, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }

    /// <summary>
    /// Gets the failure category.
    /// </summary>
    public FileStorageFailureReason Reason { get; }
}
