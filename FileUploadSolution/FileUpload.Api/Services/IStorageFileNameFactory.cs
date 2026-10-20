namespace FileUpload.Api.Services;

/// <summary>
/// A server-generated identity and file name.
/// </summary>
/// <param name="FileId">The file identifier returned to API clients.</param>
/// <param name="StoredFileName">The file name used inside the storage folder.</param>
public readonly record struct GeneratedStorageName(string FileId, string StoredFileName);

/// <summary>
/// Creates storage file names that do not depend on client input other than a validated extension.
/// </summary>
public interface IStorageFileNameFactory
{
    /// <summary>
    /// Creates a new unique storage name.
    /// </summary>
    /// <param name="validatedExtension">An allowlisted extension, including the leading dot.</param>
    GeneratedStorageName Create(string validatedExtension);
}
