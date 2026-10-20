namespace FileUpload.Api.Options;

/// <summary>
/// Strongly typed file-storage settings bound from the <c>FileStorage</c> configuration section.
/// </summary>
/// <remarks>
/// Deployment-specific values, including <see cref="SharedFolderPath"/>, should be supplied by
/// environment variables such as <c>FileStorage__SharedFolderPath</c> or by an environment-specific
/// configuration file. Do not store network passwords in configuration. The process identity is used
/// to access the folder.
/// </remarks>
public sealed class FileStorageOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "FileStorage";

    /// <summary>
    /// Absolute local path or UNC path of the folder that receives uploaded files.
    /// </summary>
    public string SharedFolderPath { get; set; } = string.Empty;

    /// <summary>
    /// Maximum size, in megabytes, accepted for a single uploaded file.
    /// </summary>
    public int MaxFileSizeInMB { get; set; } = 20;

    /// <summary>
    /// File extensions that may be stored. Each value includes the leading dot, for example <c>.pdf</c>.
    /// </summary>
    public string[] AllowedExtensions { get; set; } = [];

    /// <summary>
    /// Gets the configured maximum file size in bytes.
    /// </summary>
    public long MaxFileSizeInBytes => MaxFileSizeInMB * 1024L * 1024L;
}
