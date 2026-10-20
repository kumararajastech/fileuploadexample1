namespace FileUpload.Api.Services;

/// <summary>
/// The extra bytes allowed above the file-size limit for multipart framing.
/// The application still rejects file content larger than <c>MaxFileSizeInMB</c>.
/// </summary>
internal static class FileUploadLimits
{
    public const long MultipartOverheadBytes = 256 * 1024;
}
