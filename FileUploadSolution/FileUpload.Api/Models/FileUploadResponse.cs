namespace FileUpload.Api.Models;

/// <summary>
/// Metadata returned after a file is stored. This model never includes the physical storage path.
/// </summary>
public sealed class FileUploadResponse
{
    /// <summary>
    /// Gets the server-generated file identifier.
    /// </summary>
    public required string FileId { get; init; }

    /// <summary>
    /// Gets a sanitized display name derived from the client file name. This is not the stored path.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets the number of bytes written to storage.
    /// </summary>
    public required long FileSizeInBytes { get; init; }

    /// <summary>
    /// Gets a content type chosen from the validated extension, or <see langword="null"/> when the
    /// extension has no safe mapping. The client-supplied content type is never returned.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Gets the UTC time when the file was stored.
    /// </summary>
    public required DateTimeOffset UploadedAtUtc { get; init; }
}
