using FileUpload.Api.Exceptions;
using FileUpload.Api.Options;
using FileUpload.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FileUpload.Tests;

public sealed class SharedFolderStorageServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fileupload-storage-" + Guid.NewGuid().ToString("N"));
    private readonly GuidStorageFileNameFactory _names = new();

    public SharedFolderStorageServiceTests()
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
    public async Task SaveAsync_WritesStreamToStorageAndReturnsMetadata()
    {
        var time = new FixedTimeProvider();
        var service = CreateRealService(TestFiles.Options(_directory), time);
        var generated = _names.Create(".txt");
        var content = "invoice-body"u8.ToArray();

        var metadata = await service.SaveAsync(new MemoryStream(content), generated.StoredFileName, CancellationToken.None);

        Assert.Equal(generated.FileId, metadata.FileId);
        Assert.Equal(generated.StoredFileName, metadata.StoredFileName);
        Assert.Equal(content.Length, metadata.FileSizeInBytes);
        Assert.Equal(time.GetUtcNow(), metadata.UploadedAtUtc);
        Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(_directory, generated.StoredFileName)));
        Assert.Null(metadata.GetType().GetProperty("Path"));
        Assert.Null(metadata.GetType().GetProperty("FullPath"));
    }

    [Fact]
    public void Create_GeneratesUniqueFileNamesAndPreservesExtension()
    {
        var generated = Enumerable.Range(0, 50).Select(_ => _names.Create(".PDF")).ToArray();

        Assert.Equal(generated.Length, generated.Select(item => item.FileId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(generated, item =>
        {
            Assert.Equal(item.FileId + ".pdf", item.StoredFileName);
            Assert.Matches(
                "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\\.pdf$",
                item.StoredFileName);
        });
    }

    [Fact]
    public async Task SaveAsync_WhenDestinationExists_DoesNotOverwrite()
    {
        var service = CreateRealService(TestFiles.Options(_directory));
        var generated = _names.Create(".txt");
        var original = "original"u8.ToArray();
        var replacement = "replacement-bytes"u8.ToArray();

        await service.SaveAsync(new MemoryStream(original), generated.StoredFileName, CancellationToken.None);
        var exception = await Assert.ThrowsAsync<FileStorageException>(() =>
            service.SaveAsync(new MemoryStream(replacement), generated.StoredFileName, CancellationToken.None));

        Assert.Equal(FileStorageFailureReason.FileAlreadyExists, exception.Reason);
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(_directory, generated.StoredFileName)));
        Assert.False(File.Exists(Path.Combine(_directory, generated.StoredFileName + ".partial")));
    }

    [Fact]
    public async Task SaveAsync_WhenFolderDoesNotExist_DoesNotCreateIt()
    {
        var missing = Path.Combine(Path.GetTempPath(), "fileupload-missing-" + Guid.NewGuid().ToString("N"));
        var service = CreateRealService(TestFiles.Options(missing));

        var exception = await Assert.ThrowsAsync<FileStorageException>(() =>
            service.SaveAsync(new MemoryStream("abc"u8.ToArray()), _names.Create(".txt").StoredFileName, CancellationToken.None));

        Assert.Equal(FileStorageFailureReason.InvalidConfiguration, exception.Reason);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public async Task SaveAsync_WhenAccessIsDenied_ThrowsAccessDenied()
    {
        var service = CreateService(
            TestFiles.Options(_directory),
            new ThrowingFileSystem(new UnauthorizedAccessException("denied UNIQUE_ACL")));

        var exception = await Assert.ThrowsAsync<FileStorageException>(() =>
            service.SaveAsync(new MemoryStream("abc"u8.ToArray()), _names.Create(".txt").StoredFileName, CancellationToken.None));

        Assert.Equal(FileStorageFailureReason.AccessDenied, exception.Reason);
    }

    [Fact]
    public async Task SaveAsync_WhenNetworkShareIsUnavailable_ThrowsShareUnavailable()
    {
        var io = new IOException("The network path was not found.");
        io.HResult = unchecked((int)0x80070035);
        var service = CreateService(TestFiles.Options(@"\\FileServer\SharedFiles"), new ThrowingFileSystem(io));

        var exception = await Assert.ThrowsAsync<FileStorageException>(() =>
            service.SaveAsync(new MemoryStream("abc"u8.ToArray()), _names.Create(".txt").StoredFileName, CancellationToken.None));

        Assert.Equal(FileStorageFailureReason.ShareUnavailable, exception.Reason);
        Assert.DoesNotContain("SharedFiles", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAsync_WhenCancelledBeforeWrite_DoesNotCreateFile()
    {
        var service = CreateRealService(TestFiles.Options(_directory));
        var generated = _names.Create(".txt");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SaveAsync(new MemoryStream("abc"u8.ToArray()), generated.StoredFileName, cancellation.Token));

        Assert.False(File.Exists(Path.Combine(_directory, generated.StoredFileName)));
        Assert.False(File.Exists(Path.Combine(_directory, generated.StoredFileName + ".partial")));
    }

    [Fact]
    public async Task SaveAsync_WhenCancelledDuringCopy_RemovesPartialFile()
    {
        var service = CreateRealService(TestFiles.Options(_directory));
        var generated = _names.Create(".txt");
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SaveAsync(new CancelAfterFirstByteStream(cancellation), generated.StoredFileName, cancellation.Token));

        Assert.False(File.Exists(Path.Combine(_directory, generated.StoredFileName)));
        Assert.False(File.Exists(Path.Combine(_directory, generated.StoredFileName + ".partial")));
    }

    [Fact]
    public async Task SaveAsync_RejectsClientSuppliedPaths()
    {
        var service = CreateRealService(TestFiles.Options(_directory));

        var exception = await Assert.ThrowsAsync<FileStorageException>(() =>
            service.SaveAsync(new MemoryStream("abc"u8.ToArray()), @"..\..\outside.txt", CancellationToken.None));

        Assert.Equal(FileStorageFailureReason.InvalidRequest, exception.Reason);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    private static SharedFolderStorageService CreateRealService(FileStorageOptions options, TimeProvider? time = null) =>
        CreateService(options, new LocalFileSystemAccessor(NullLogger<LocalFileSystemAccessor>.Instance), time);

    private static SharedFolderStorageService CreateService(
        FileStorageOptions options,
        IFileSystemAccessor fileSystem,
        TimeProvider? time = null) =>
        new(
            new TestOptionsMonitor<FileStorageOptions>(options),
            fileSystem,
            time ?? TimeProvider.System,
            NullLogger<SharedFolderStorageService>.Instance);

    private sealed class ThrowingFileSystem : IFileSystemAccessor
    {
        private readonly Exception _exception;

        public ThrowingFileSystem(Exception exception)
        {
            _exception = exception;
        }

        public void EnsureDirectoryReady(string fullPath) => throw _exception;

        public Task<long> WriteNewAsync(string fullPath, Stream content, long maximumBytes, CancellationToken cancellationToken) =>
            throw _exception;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 19, 16, 0, 0, TimeSpan.Zero);
    }

    private sealed class CancelAfterFirstByteStream : Stream
    {
        private readonly CancellationTokenSource _cancellation;
        private int _reads;

        public CancelAfterFirstByteStream(CancellationTokenSource cancellation)
        {
            _cancellation = cancellation;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadCore(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ReadCore(buffer, cancellationToken);

        private ValueTask<int> ReadCore(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_reads++ > 0)
            {
                _cancellation.Cancel();
                throw new OperationCanceledException(_cancellation.Token);
            }

            buffer.Span[0] = 1;
            return ValueTask.FromResult(1);
        }
    }
}

public sealed class FileStorageOptionsValidatorTests
{
    private readonly FileStorageOptionsValidator _validator = new();

    [Fact]
    public void Validate_AcceptsUncAndLocalPaths()
    {
        Assert.False(_validator.Validate(null, Options(@"\\FileServer\SharedFiles")).Failed);
        Assert.False(_validator.Validate(null, Options(@"D:\Departments\Uploads")).Failed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("uploads")]
    [InlineData(@"..\uploads")]
    [InlineData(@"\\FileServer\SharedFiles\..\Other")]
    [InlineData(@"\\user:password@FileServer\SharedFiles")]
    public void Validate_RejectsMissingRelativeOrCredentialPaths(string path)
    {
        var result = _validator.Validate(null, Options(path));

        Assert.True(result.Failed);
        Assert.DoesNotContain("password", string.Join(" ", result.Failures), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(512)]
    public void Validate_AcceptsSizeBoundaries(int megabytes)
    {
        var options = Options(@"C:\storage");
        options.MaxFileSizeInMB = megabytes;

        Assert.False(_validator.Validate(null, options).Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(513)]
    public void Validate_RejectsSizeOutsideRange(int megabytes)
    {
        var options = Options(@"C:\storage");
        options.MaxFileSizeInMB = megabytes;

        Assert.True(_validator.Validate(null, options).Failed);
    }

    [Fact]
    public void Validate_RejectsEmptyMalformedAndExecutableExtensions()
    {
        Assert.True(_validator.Validate(null, Options(@"C:\storage", [])).Failed);
        Assert.True(_validator.Validate(null, Options(@"C:\storage", ["pdf"])).Failed);
        Assert.True(_validator.Validate(null, Options(@"C:\storage", [".pdf", ".exe"])).Failed);
        Assert.True(_validator.Validate(null, Options(@"C:\storage", [@".pdf\.."])).Failed);
    }

    private static FileStorageOptions Options(string path, string[]? extensions = null) => new()
    {
        SharedFolderPath = path,
        MaxFileSizeInMB = 20,
        AllowedExtensions = extensions ?? [".pdf", ".txt"]
    };
}

internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    public TestOptionsMonitor(T value)
    {
        CurrentValue = value;
    }

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string?> listener) => EmptyDisposable.Instance;

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}

internal static class TestFiles
{
    public static FileStorageOptions Options(string path, int maxFileSizeInMb = 20) => new()
    {
        SharedFolderPath = path,
        MaxFileSizeInMB = maxFileSizeInMb,
        AllowedExtensions = [".pdf", ".docx", ".xlsx", ".csv", ".txt", ".png", ".jpg", ".jpeg"]
    };

    public static FormFile FormFile(string fileName, byte[] content, string contentType, long? declaredLength = null) =>
        new(new MemoryStream(content), 0, declaredLength ?? content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
}
