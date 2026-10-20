using System.Text.Json;
using FileUpload.Api.Controllers;
using FileUpload.Api.Models;
using FileUpload.Api.Options;
using FileUpload.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FileUpload.Tests;

public sealed class FilesControllerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fileupload-controller-" + Guid.NewGuid().ToString("N"));

    public FilesControllerTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Upload_ValidFile_ReturnsCreatedAndStoresGeneratedName()
    {
        var service = CreateRealService(TestFiles.Options(_directory));
        var controller = CreateController(TestFiles.Options(_directory), service);
        var file = TestFiles.FormFile("document.pdf", "hello"u8.ToArray(), "application/x-msdownload");

        var result = await controller.Upload(file, CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var body = Assert.IsType<FileUploadResponse>(created.Value);
        Assert.Equal("document.pdf", body.FileName);
        Assert.Equal("application/pdf", body.ContentType);
        Assert.Equal(5, body.FileSizeInBytes);
        Assert.Matches("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", body.FileId);

        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"fileId\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain(_directory, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("application/x-msdownload", json, StringComparison.OrdinalIgnoreCase);

        var storedPath = Path.Combine(_directory, body.FileId + ".pdf");
        Assert.Equal("hello", await File.ReadAllTextAsync(storedPath));
        Assert.DoesNotContain("document.pdf", Directory.GetFiles(_directory).Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upload_TwoFiles_UsesUniqueServerFileNames()
    {
        var storage = new RecordingStorage();
        var controller = CreateController(TestFiles.Options(_directory), storage);

        await controller.Upload(TestFiles.FormFile("document.pdf", "one"u8.ToArray(), "text/plain"), CancellationToken.None);
        await controller.Upload(TestFiles.FormFile("document.pdf", "two"u8.ToArray(), "text/plain"), CancellationToken.None);

        Assert.Equal(2, storage.Names.Count);
        Assert.NotEqual(storage.Names[0], storage.Names[1]);
        Assert.All(storage.Names, name =>
        {
            Assert.DoesNotContain("document", name, StringComparison.OrdinalIgnoreCase);
            Assert.Matches("^[0-9a-fA-F-]{36}\\.pdf$", name);
        });
    }

    [Fact]
    public async Task Upload_MissingFile_Returns400()
    {
        var controller = CreateController(TestFiles.Options(_directory), new RecordingStorage());

        var result = await controller.Upload(null, CancellationToken.None);

        var error = AssertError(result, StatusCodes.Status400BadRequest, "file_required");
        Assert.Equal("A file is required.", error.Detail);
    }

    [Fact]
    public async Task Upload_EmptyFile_Returns400()
    {
        var controller = CreateController(TestFiles.Options(_directory), new RecordingStorage());

        var result = await controller.Upload(TestFiles.FormFile("empty.txt", [], "text/plain"), CancellationToken.None);

        AssertError(result, StatusCodes.Status400BadRequest, "file_empty");
    }

    [Fact]
    public async Task Upload_UnsupportedExtension_Returns400()
    {
        var storage = new RecordingStorage();
        var controller = CreateController(TestFiles.Options(_directory), storage);

        var result = await controller.Upload(TestFiles.FormFile("program.exe", "abc"u8.ToArray(), "application/pdf"), CancellationToken.None);

        AssertError(result, StatusCodes.Status400BadRequest, "file_extension_not_allowed");
        Assert.Empty(storage.Names);
    }

    [Fact]
    public async Task Upload_EmbeddedExecutableExtension_Returns400()
    {
        var controller = CreateController(TestFiles.Options(_directory), new RecordingStorage());

        var result = await controller.Upload(TestFiles.FormFile("invoice.exe.pdf", "abc"u8.ToArray(), "application/pdf"), CancellationToken.None);

        AssertError(result, StatusCodes.Status400BadRequest, "file_extension_not_allowed");
    }

    [Fact]
    public async Task Upload_FileLargerThanConfiguredLimit_Returns413()
    {
        var options = TestFiles.Options(_directory, maxFileSizeInMb: 1);
        var storage = new RecordingStorage();
        var controller = CreateController(options, storage);
        var file = TestFiles.FormFile("big.pdf", [1], "application/pdf", declaredLength: options.MaxFileSizeInBytes + 1);

        var result = await controller.Upload(file, CancellationToken.None);

        AssertError(result, StatusCodes.Status413PayloadTooLarge, "file_too_large");
        Assert.Empty(storage.Names);
    }

    [Fact]
    public async Task Upload_FileAtConfiguredLimit_Returns201()
    {
        var options = TestFiles.Options(_directory, maxFileSizeInMb: 1);
        var storage = new RecordingStorage();
        var controller = CreateController(options, storage);
        var file = TestFiles.FormFile("limit.pdf", [1, 2, 3], "application/octet-stream", declaredLength: options.MaxFileSizeInBytes);

        var result = await controller.Upload(file, CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Single(storage.Names);
    }

    [Fact]
    public async Task Upload_UppercaseExtension_IsAcceptedAndContentTypeComesFromExtension()
    {
        var storage = new RecordingStorage();
        var controller = CreateController(TestFiles.Options(_directory), storage);

        var result = await controller.Upload(TestFiles.FormFile(@"..\..\secret.PDF", "abc"u8.ToArray(), "text/html"), CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result);
        var body = Assert.IsType<FileUploadResponse>(created.Value);
        Assert.Equal("secret.PDF", body.FileName);
        Assert.Equal("application/pdf", body.ContentType);
        Assert.EndsWith(".pdf", storage.Names.Single(), StringComparison.Ordinal);
        Assert.DoesNotContain("..", storage.Names.Single(), StringComparison.Ordinal);
    }

    private static ApiErrorResponse AssertError(IActionResult result, int statusCode, string errorCode)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(statusCode, objectResult.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(objectResult.Value);
        Assert.Equal(statusCode, error.Status);
        Assert.Equal(errorCode, error.ErrorCode);
        Assert.DoesNotContain("\\", error.Detail, StringComparison.Ordinal);
        return error;
    }

    private static FilesController CreateController(FileStorageOptions options, IFileStorageService storage)
    {
        var controller = new FilesController(
            storage,
            new GuidStorageFileNameFactory(),
            new TestOptionsMonitor<FileStorageOptions>(options),
            NullLogger<FilesController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        return controller;
    }

    private static SharedFolderStorageService CreateRealService(FileStorageOptions options) =>
        new(
            new TestOptionsMonitor<FileStorageOptions>(options),
            new LocalFileSystemAccessor(NullLogger<LocalFileSystemAccessor>.Instance),
            TimeProvider.System,
            NullLogger<SharedFolderStorageService>.Instance);

    private sealed class RecordingStorage : IFileStorageService
    {
        public List<string> Names { get; } = [];

        public Task<StoredFileMetadata> SaveAsync(Stream content, string storageFileName, CancellationToken cancellationToken)
        {
            Names.Add(storageFileName);
            return Task.FromResult(new StoredFileMetadata
            {
                FileId = Path.GetFileNameWithoutExtension(storageFileName),
                StoredFileName = storageFileName,
                FileSizeInBytes = 3,
                UploadedAtUtc = new DateTimeOffset(2026, 10, 19, 16, 0, 0, TimeSpan.Zero)
            });
        }
    }
}
