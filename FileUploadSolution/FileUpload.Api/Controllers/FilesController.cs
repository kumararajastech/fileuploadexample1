using FileUpload.Api.Models;
using FileUpload.Api.Options;
using FileUpload.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FileUpload.Api.Controllers;

/// <summary>
/// Accepts multipart file uploads and delegates storage to the configured file-storage service.
/// </summary>
[ApiController]
[Route("api/files")]
[Produces("application/json")]
public sealed class FilesController : ControllerBase
{
    private readonly IFileStorageService _storage;
    private readonly IStorageFileNameFactory _fileNames;
    private readonly IOptionsMonitor<FileStorageOptions> _options;
    private readonly ILogger<FilesController> _logger;

    /// <summary>
    /// Creates the controller.
    /// </summary>
    public FilesController(
        IFileStorageService storage,
        IStorageFileNameFactory fileNames,
        IOptionsMonitor<FileStorageOptions> options,
        ILogger<FilesController> logger)
    {
        _storage = storage;
        _fileNames = fileNames;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Uploads one file to the configured shared folder.
    /// </summary>
    /// <param name="file">The file supplied as the <c>file</c> form field. Its name and content type are not trusted.</param>
    /// <param name="cancellationToken">A token that cancels the upload.</param>
    /// <returns>The stored file's public metadata.</returns>
    /// <response code="201">The file was stored.</response>
    /// <response code="400">The request did not contain a valid file.</response>
    /// <response code="401">Authentication is required and was not supplied.</response>
    /// <response code="403">The caller is not allowed to upload files.</response>
    /// <response code="413">The file exceeds the configured size limit.</response>
    /// <response code="500">The file could not be stored because of an unexpected failure.</response>
    /// <response code="503">The storage location is temporarily unavailable.</response>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileUploadResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var validation = FileUploadRules.Evaluate(file, options);
        if (!validation.Succeeded)
        {
            _logger.LogWarning(
                "Rejected upload. ErrorCode: {ErrorCode}. Length: {Length}.",
                validation.ErrorCode,
                file?.Length);

            return Error(validation.StatusCode!.Value, TitleFor(validation.StatusCode.Value), validation.Detail!, validation.ErrorCode!);
        }

        var generated = _fileNames.Create(validation.Extension!);
        await using var content = file!.OpenReadStream();
        var stored = await _storage.SaveAsync(content, generated.StoredFileName, cancellationToken);

        var response = new FileUploadResponse
        {
            FileId = stored.FileId,
            FileName = validation.DisplayName!,
            FileSizeInBytes = stored.FileSizeInBytes,
            ContentType = FileContentTypes.FromExtension(validation.Extension!),
            UploadedAtUtc = stored.UploadedAtUtc
        };

        _logger.LogInformation(
            "Upload completed. FileId: {FileId}. Size: {FileSizeInBytes} bytes.",
            response.FileId,
            response.FileSizeInBytes);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    private ObjectResult Error(int statusCode, string title, string detail, string errorCode) =>
        new(new ApiErrorResponse
        {
            Title = title,
            Status = statusCode,
            Detail = detail,
            ErrorCode = errorCode,
            TraceId = HttpContext?.TraceIdentifier
        })
        {
            StatusCode = statusCode
        };

    private static string TitleFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status413PayloadTooLarge => "Payload Too Large",
        _ => "Bad Request"
    };
}
