using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileUpload.Api.Exceptions;
using FileUpload.Api.Models;

namespace FileUpload.Api.Middleware;

/// <summary>
/// Converts unhandled exceptions into a consistent <see cref="ApiErrorResponse"/> and logs the diagnostic detail.
/// </summary>
public sealed class GlobalExceptionMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    /// <summary>
    /// Creates the middleware.
    /// </summary>
    /// <param name="next">The next request delegate.</param>
    /// <param name="logger">The logger used for diagnostic information.</param>
    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the rest of the pipeline and writes a safe error response when it fails.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["TraceId"] = context.TraceIdentifier
        }))
        {
            try
            {
                await _next(context);
            }
            catch (Exception exception)
            {
                await HandleAsync(context, exception);
            }
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation("The request was cancelled. TraceId: {TraceId}.", context.TraceIdentifier);
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        var error = Map(exception);
        if (error.LogAsError)
        {
            _logger.LogError(exception, "Request failed. TraceId: {TraceId}. ErrorCode: {ErrorCode}.", context.TraceIdentifier, error.Body.ErrorCode);
        }
        else
        {
            _logger.LogWarning("Request rejected. TraceId: {TraceId}. ErrorCode: {ErrorCode}.", context.TraceIdentifier, error.Body.ErrorCode);
        }

        if (context.Response.HasStarted)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        context.Response.Clear();
        context.Response.StatusCode = error.Body.Status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Cache-Control"] = "no-store";

        var body = new ApiErrorResponse
        {
            Title = error.Body.Title,
            Status = error.Body.Status,
            Detail = error.Body.Detail,
            ErrorCode = error.Body.ErrorCode,
            TraceId = context.TraceIdentifier
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions), context.RequestAborted);
    }

    private static MappedError Map(Exception exception)
    {
        if (exception is FileStorageException storage)
        {
            return storage.Reason switch
            {
                FileStorageFailureReason.InvalidRequest => Client(
                    StatusCodes.Status400BadRequest,
                    "Bad Request",
                    "The upload request is invalid.",
                    "invalid_request"),
                FileStorageFailureReason.FileTooLarge => Client(
                    StatusCodes.Status413PayloadTooLarge,
                    "Payload Too Large",
                    "The file exceeds the maximum allowed size.",
                    "file_too_large"),
                FileStorageFailureReason.AccessDenied or FileStorageFailureReason.ShareUnavailable => Server(
                    StatusCodes.Status503ServiceUnavailable,
                    "Service Unavailable",
                    "The file storage service is temporarily unavailable.",
                    "storage_unavailable"),
                _ => Server(
                    StatusCodes.Status500InternalServerError,
                    "Internal Server Error",
                    "The file could not be stored.",
                    "storage_failure")
            };
        }

        if (IsPayloadTooLarge(exception))
        {
            return Client(
                StatusCodes.Status413PayloadTooLarge,
                "Payload Too Large",
                "The file exceeds the maximum allowed size.",
                "file_too_large");
        }

        if (exception is BadHttpRequestException badRequest)
        {
            var status = badRequest.StatusCode is >= 400 and <= 499
                ? badRequest.StatusCode
                : StatusCodes.Status400BadRequest;
            return Client(status, "Bad Request", "The request is invalid.", "invalid_request");
        }

        if (exception is OperationCanceledException)
        {
            return Client(StatusCodes.Status400BadRequest, "Bad Request", "The request was cancelled.", "request_cancelled");
        }

        return Server(
            StatusCodes.Status500InternalServerError,
            "Internal Server Error",
            "An unexpected error occurred.",
            "unexpected_error");
    }

    private static bool IsPayloadTooLarge(Exception exception)
    {
        if (exception is BadHttpRequestException badRequest &&
            badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return true;
        }

        return exception is InvalidDataException &&
            exception.Message.Contains("length", StringComparison.OrdinalIgnoreCase) &&
            exception.Message.Contains("exceed", StringComparison.OrdinalIgnoreCase);
    }

    private static MappedError Client(int status, string title, string detail, string errorCode) =>
        new(false, Create(status, title, detail, errorCode));

    private static MappedError Server(int status, string title, string detail, string errorCode) =>
        new(true, Create(status, title, detail, errorCode));

    private static ApiErrorResponse Create(int status, string title, string detail, string errorCode) =>
        new()
        {
            Title = title,
            Status = status,
            Detail = detail,
            ErrorCode = errorCode
        };

    private readonly record struct MappedError(bool LogAsError, ApiErrorResponse Body);
}
