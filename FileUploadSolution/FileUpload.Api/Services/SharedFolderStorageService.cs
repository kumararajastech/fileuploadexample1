using FileUpload.Api.Exceptions;
using FileUpload.Api.Models;
using FileUpload.Api.Options;
using Microsoft.Extensions.Options;

namespace FileUpload.Api.Services;

/// <summary>
/// Stores uploads in the configured shared folder. The service is stateless and safe to register as a singleton:
/// each call uses its own streams, and the file name is unique to that call.
/// </summary>
public sealed class SharedFolderStorageService : IFileStorageService
{
    private readonly IOptionsMonitor<FileStorageOptions> _options;
    private readonly IFileSystemAccessor _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SharedFolderStorageService> _logger;

    /// <summary>
    /// Creates the storage service.
    /// </summary>
    public SharedFolderStorageService(
        IOptionsMonitor<FileStorageOptions> options,
        IFileSystemAccessor fileSystem,
        TimeProvider timeProvider,
        ILogger<SharedFolderStorageService> logger)
    {
        _options = options;
        _fileSystem = fileSystem;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<StoredFileMetadata> SaveAsync(
        Stream content,
        string storageFileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        var options = _options.CurrentValue;
        StorageFileNameRules.EnsureSafe(storageFileName, options.AllowedExtensions);

        var fileId = Path.GetFileNameWithoutExtension(storageFileName);
        var root = StorageFileNameRules.NormalizeRoot(options.SharedFolderPath);
        var destination = StorageFileNameRules.CombineUnderRoot(root, storageFileName);

        try
        {
            _fileSystem.EnsureDirectoryReady(root);
            var uploadedAt = _timeProvider.GetUtcNow();
            var size = await _fileSystem
                .WriteNewAsync(destination, content, options.MaxFileSizeInBytes, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Stored file {FileId}. Size: {FileSizeInBytes} bytes.",
                fileId,
                size);

            return new StoredFileMetadata
            {
                FileId = fileId,
                StoredFileName = storageFileName,
                FileSizeInBytes = size,
                UploadedAtUtc = uploadedAt
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Storage was cancelled for file {FileId}.", fileId);
            throw;
        }
        catch (FileStorageException exception)
        {
            LogFailure(exception, fileId, root);
            throw;
        }
        catch (Exception exception)
        {
            var mapped = StorageIo.Map(exception, root);
            LogFailure(mapped, fileId, root);
            throw mapped;
        }
    }

    private void LogFailure(FileStorageException exception, string fileId, string storageRoot)
    {
        if (exception.Reason is FileStorageFailureReason.InvalidRequest or FileStorageFailureReason.FileTooLarge)
        {
            _logger.LogWarning(
                "Storage rejected file {FileId}. Reason: {Reason}.",
                fileId,
                exception.Reason);
            return;
        }

        _logger.LogError(
            exception,
            "Storage failed for file {FileId}. Reason: {Reason}. StorageRoot: {StorageRoot}.",
            fileId,
            exception.Reason,
            storageRoot);
    }
}
