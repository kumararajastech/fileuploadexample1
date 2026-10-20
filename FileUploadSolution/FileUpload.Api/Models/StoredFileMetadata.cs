namespace FileUpload.Api.Models;

/// <summary>
/// Storage metadata returned to the application. It intentionally has no physical path property.
/// </summary>
public sealed class StoredFileMetadata
{
    /// <summary>
    /// Gets the server-generated file identifier. This matches the stored file name without its extension.
    /// </summary>
    public required string FileId { get; init; }

    /// <summary>
    /// Gets the server-generated file name, including the validated extension.
    /// </summary>
    public required string StoredFileName { get; init; }

    /// <summary>
    /// Gets the number of bytes written.
    /// </summary>
    public required long FileSizeInBytes { get; init; }

    /// <summary>
    /// Gets the UTC timestamp taken when the write started.
    /// </summary>
    public required DateTimeOffset UploadedAtUtc { get; init; }
}
