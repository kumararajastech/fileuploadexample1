namespace FileUpload.Api.Services;

/// <summary>
/// File-system operations used by <see cref="SharedFolderStorageService"/>.
/// Tests can replace this with an isolated implementation.
/// </summary>
public interface IFileSystemAccessor
{
    /// <summary>
    /// Confirms that the directory exists and is accessible. This method must not create the directory.
    /// </summary>
    /// <param name="fullPath">The normalized storage root.</param>
    void EnsureDirectoryReady(string fullPath);

    /// <summary>
    /// Creates a new file and copies the stream into it. Existing files are not overwritten.
    /// </summary>
    /// <param name="fullPath">The destination file path under the storage root.</param>
    /// <param name="content">The source stream.</param>
    /// <param name="maximumBytes">The maximum number of bytes that may be copied.</param>
    /// <param name="cancellationToken">A token that cancels the copy.</param>
    /// <returns>The number of bytes written.</returns>
    Task<long> WriteNewAsync(
        string fullPath,
        Stream content,
        long maximumBytes,
        CancellationToken cancellationToken);
}
