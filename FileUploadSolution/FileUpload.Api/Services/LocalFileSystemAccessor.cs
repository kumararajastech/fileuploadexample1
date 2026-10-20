using FileUpload.Api.Exceptions;

namespace FileUpload.Api.Services;

/// <summary>
/// Writes files to a local directory or a UNC share. It never creates the configured root folder.
/// </summary>
internal sealed class LocalFileSystemAccessor : IFileSystemAccessor
{
    private readonly ILogger<LocalFileSystemAccessor> _logger;

    public LocalFileSystemAccessor(ILogger<LocalFileSystemAccessor> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void EnsureDirectoryReady(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(fullPath);
        }
        catch (Exception exception)
        {
            throw StorageIo.Map(exception, fullPath);
        }

        if ((attributes & FileAttributes.Directory) == 0)
        {
            throw new FileStorageException(
                FileStorageFailureReason.InvalidConfiguration,
                "The configured storage path is not a folder.");
        }
    }

    /// <inheritdoc />
    public async Task<long> WriteNewAsync(
        string fullPath,
        Stream content,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentNullException.ThrowIfNull(content);

        var partialPath = fullPath + ".partial";
        var moved = false;

        try
        {
            long total;
            await using (var destination = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                total = await CopyLimitedAsync(content, destination, maximumBytes, cancellationToken)
                    .ConfigureAwait(false);

                if (total == 0)
                {
                    throw new FileStorageException(
                        FileStorageFailureReason.InvalidRequest,
                        "The file is empty.");
                }

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(partialPath, fullPath);
            moved = true;
            return total;
        }
        catch (Exception exception) when (exception is not FileStorageException && StorageIo.IsFileAlreadyExists(exception))
        {
            throw new FileStorageException(
                FileStorageFailureReason.FileAlreadyExists,
                "A file with the generated name already exists.",
                exception);
        }
        finally
        {
            if (!moved)
            {
                DeletePartial(partialPath);
            }
        }
    }

    private static async Task<long> CopyLimitedAsync(
        Stream source,
        Stream destination,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            long total = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    return total;
                }

                total += read;
                if (total > maximumBytes)
                {
                    throw new FileStorageException(
                        FileStorageFailureReason.FileTooLarge,
                        "The file exceeds the maximum allowed size.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void DeletePartial(string partialPath)
    {
        try
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to remove an incomplete upload.");
        }
    }
}
