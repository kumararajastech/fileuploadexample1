using FileUpload.Api.Models;

namespace FileUpload.Api.Services;

/// <summary>
/// Stores a validated file stream in the configured shared folder.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Writes <paramref name="content"/> to storage using a server-generated file name.
    /// </summary>
    /// <param name="content">The file content. The caller retains ownership of the stream.</param>
    /// <param name="storageFileName">A server-generated file name. Client file names are rejected.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>Metadata that does not include the physical storage path.</returns>
    Task<StoredFileMetadata> SaveAsync(
        Stream content,
        string storageFileName,
        CancellationToken cancellationToken);
}
