namespace FileUpload.Api.Services;

/// <summary>
/// Generates GUID file names and preserves only a previously validated extension.
/// </summary>
public sealed class GuidStorageFileNameFactory : IStorageFileNameFactory
{
    /// <inheritdoc />
    public GeneratedStorageName Create(string validatedExtension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(validatedExtension);

        if (!StorageFileNameRules.IsSafeExtension(validatedExtension))
        {
            throw new ArgumentException("The file extension is invalid.", nameof(validatedExtension));
        }

        var fileId = Guid.NewGuid().ToString("D");
        var storedFileName = string.Concat(fileId, validatedExtension.ToLowerInvariant());
        return new GeneratedStorageName(fileId, storedFileName);
    }
}
