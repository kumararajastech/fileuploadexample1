using FileUpload.Api.Options;
using Microsoft.Extensions.Options;

namespace FileUpload.Api.Services;

/// <summary>
/// Reports whether the configured folder is reachable when the process starts.
/// A missing or disconnected share is logged and does not cause the folder to be created.
/// </summary>
internal sealed class FileStorageStartupCheck : IHostedService
{
    private readonly IOptionsMonitor<FileStorageOptions> _options;
    private readonly IFileSystemAccessor _fileSystem;
    private readonly ILogger<FileStorageStartupCheck> _logger;

    public FileStorageStartupCheck(
        IOptionsMonitor<FileStorageOptions> options,
        IFileSystemAccessor fileSystem,
        ILogger<FileStorageStartupCheck> logger)
    {
        _options = options;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var configuredPath = _options.CurrentValue.SharedFolderPath;
        try
        {
            var root = StorageFileNameRules.NormalizeRoot(configuredPath);
            _fileSystem.EnsureDirectoryReady(root);
            _logger.LogInformation("File storage folder {StorageRoot} is accessible.", root);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "File storage is not accessible. Uploads will fail until the folder is available. The application will not create it.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
