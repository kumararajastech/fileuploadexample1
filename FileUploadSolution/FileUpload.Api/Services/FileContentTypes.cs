namespace FileUpload.Api.Services;

/// <summary>
/// Content types derived from a validated extension. Client-supplied content types are ignored.
/// </summary>
internal static class FileContentTypes
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".csv"] = "text/csv",
        [".txt"] = "text/plain",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg"
    };

    public static string? FromExtension(string extension) =>
        ByExtension.TryGetValue(extension, out var contentType) ? contentType : null;
}
