using System.Text;
using System.Text.Json;
using FileUpload.Api.Exceptions;
using FileUpload.Api.Middleware;
using FileUpload.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileUpload.Tests;

public sealed class GlobalExceptionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenStorageAccessIsDenied_Returns503WithoutInternalDetails()
    {
        var json = await InvokeAsync(new FileStorageException(
            FileStorageFailureReason.AccessDenied,
            @"Access is denied for \\FileServer\SecretShare",
            new UnauthorizedAccessException("UNIQUE_SECRET_DETAIL")));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, json.Status);
        Assert.Equal("storage_unavailable", json.Body.ErrorCode);
        AssertSafe(json.Json);
    }

    [Fact]
    public async Task InvokeAsync_WhenNetworkShareIsUnavailable_Returns503WithoutInternalDetails()
    {
        var json = await InvokeAsync(new FileStorageException(
            FileStorageFailureReason.ShareUnavailable,
            @"The network path \\FileServer\SharedFiles was not found."));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, json.Status);
        Assert.Equal("storage_unavailable", json.Body.ErrorCode);
        AssertSafe(json.Json);
    }

    [Fact]
    public async Task InvokeAsync_WhenFolderIsMissing_Returns500WithoutPath()
    {
        var json = await InvokeAsync(new FileStorageException(
            FileStorageFailureReason.InvalidConfiguration,
            @"The folder D:\secret-share does not exist."));

        Assert.Equal(StatusCodes.Status500InternalServerError, json.Status);
        Assert.Equal("storage_failure", json.Body.ErrorCode);
        AssertSafe(json.Json);
    }

    [Fact]
    public async Task InvokeAsync_WhenFileIsTooLarge_Returns413()
    {
        var json = await InvokeAsync(new FileStorageException(
            FileStorageFailureReason.FileTooLarge,
            "The file exceeds the maximum allowed size."));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, json.Status);
        Assert.Equal("file_too_large", json.Body.ErrorCode);
    }

    [Fact]
    public async Task InvokeAsync_WhenMultipartLimitIsExceeded_Returns413()
    {
        var json = await InvokeAsync(new InvalidDataException("Multipart body length limit 1048576 exceeded."));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, json.Status);
        Assert.DoesNotContain("1048576", json.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_WhenUnexpectedException_Returns500WithoutStackTrace()
    {
        var json = await InvokeAsync(new InvalidOperationException("UNIQUE_INTERNAL_DETAIL at D:\\secret-share"));

        Assert.Equal(StatusCodes.Status500InternalServerError, json.Status);
        Assert.Equal("unexpected_error", json.Body.ErrorCode);
        Assert.Equal("An unexpected error occurred.", json.Body.Detail);
        AssertSafe(json.Json);
    }

    [Fact]
    public async Task InvokeAsync_WhenRequestIsAborted_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new DefaultHttpContext
        {
            RequestAborted = cancellation.Token,
            Response = { Body = new MemoryStream() }
        };
        var middleware = new GlobalExceptionMiddleware(
            _ => throw new OperationCanceledException(cancellation.Token),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => middleware.InvokeAsync(context));
    }

    private static async Task<(int Status, ApiErrorResponse Body, string Json)> InvokeAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.TraceIdentifier = "trace-123";
        var middleware = new GlobalExceptionMiddleware(
            _ => throw exception,
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var json = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        var body = JsonSerializer.Deserialize<ApiErrorResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(body);
        Assert.Equal("trace-123", body.TraceId);
        return (context.Response.StatusCode, body, json);
    }

    private static void AssertSafe(string json)
    {
        Assert.DoesNotContain("Secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UNIQUE", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"\\", json, StringComparison.Ordinal);
        Assert.DoesNotContain("D:", json, StringComparison.OrdinalIgnoreCase);
    }
}
