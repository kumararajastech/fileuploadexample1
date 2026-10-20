namespace FileUpload.Api.Models;

/// <summary>
/// A safe error body. It does not include stack traces, internal exception messages, or filesystem paths.
/// </summary>
public sealed class ApiErrorResponse
{
    /// <summary>
    /// Gets a short title for the HTTP status.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the HTTP status code.
    /// </summary>
    public required int Status { get; init; }

    /// <summary>
    /// Gets a client-safe explanation.
    /// </summary>
    public required string Detail { get; init; }

    /// <summary>
    /// Gets a stable application error code.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Gets the request trace identifier, when one is available.
    /// </summary>
    public string? TraceId { get; init; }
}
