using System.Text.Json.Serialization;
using FileUpload.Api.Exceptions;
using FileUpload.Api.Middleware;
using FileUpload.Api.Models;
using FileUpload.Api.Options;
using FileUpload.Api.Services;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IValidateOptions<FileStorageOptions>, FileStorageOptionsValidator>();
builder.Services.AddOptions<FileStorageOptions>()
    .Bind(builder.Configuration.GetSection(FileStorageOptions.SectionName))
    .ValidateOnStart();

var requireAuthentication = builder.Configuration.GetValue($"{SecurityOptions.SectionName}:RequireAuthentication", true);
if (!requireAuthentication && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Security:RequireAuthentication cannot be false outside the Development environment.");
}

var transportLimit = ResolveTransportLimit(builder.Configuration);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = transportLimit;
});

builder.Services.AddOptions<FormOptions>()
    .Configure(options =>
    {
        options.MultipartBodyLengthLimit = transportLimit;
        options.BufferBodyLengthLimit = transportLimit;
        options.MemoryBufferThreshold = 64 * 1024;
        options.ValueCountLimit = 8;
        options.KeyLengthLimit = 256;
        options.ValueLengthLimit = 1024 * 1024;
        options.MultipartHeadersLengthLimit = 16 * 1024;
    });

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "File Upload API",
        Version = "v1",
        Description =
            "Uploads files into a configured shared folder. Client file names and content types are not trusted. " +
            "Responses never include the physical storage path."
    });

    options.MapType<IFormFile>(() => new OpenApiSchema
    {
        Type = JsonSchemaType.String,
        Format = "binary"
    });

    var xmlPath = Path.Combine(AppContext.BaseDirectory, "FileUpload.Api.xml");
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }
});

if (requireAuthentication)
{
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate();

    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    });
}
else
{
    builder.Services.AddAuthorization();
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IFileSystemAccessor, LocalFileSystemAccessor>();
builder.Services.AddSingleton<IStorageFileNameFactory, GuidStorageFileNameFactory>();
// Singleton: the storage service holds no per-request state. Concurrent uploads use distinct streams and file names.
builder.Services.AddSingleton<IFileStorageService, SharedFolderStorageService>();
builder.Services.AddHostedService<FileStorageStartupCheck>();

var app = builder.Build();

if (!requireAuthentication)
{
    app.Logger.LogWarning(
        "Authentication is disabled because Security:RequireAuthentication is false in Development. Do not use this configuration in production.");
}

app.UseForwardedHeaders();
app.UseMiddleware<GlobalExceptionMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Cache-Control"] = "no-store";

    var configuredLimit = context.RequestServices
        .GetRequiredService<IOptionsMonitor<FileStorageOptions>>()
        .CurrentValue
        .MaxFileSizeInBytes + FileUploadLimits.MultipartOverheadBytes;
    var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (feature is { IsReadOnly: false })
    {
        feature.MaxRequestBodySize = Math.Min(configuredLimit, transportLimit);
    }

    await next();
});

app.UseStatusCodePages(async statusCodeContext =>
{
    var http = statusCodeContext.HttpContext;
    if (http.Response.StatusCode is not (StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden) ||
        http.Response.HasStarted)
    {
        return;
    }

    var (title, detail, errorCode) = http.Response.StatusCode == StatusCodes.Status401Unauthorized
        ? ("Unauthorized", "Authentication is required.", "unauthorized")
        : ("Forbidden", "You do not have permission to perform this action.", "forbidden");

    http.Response.ContentType = "application/json; charset=utf-8";
    await http.Response.WriteAsJsonAsync(
        new ApiErrorResponse
        {
            Title = title,
            Status = http.Response.StatusCode,
            Detail = detail,
            ErrorCode = errorCode,
            TraceId = http.TraceIdentifier
        },
        http.RequestAborted);
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "File Upload API v1");
        options.RoutePrefix = "swagger";
    });
}

if (requireAuthentication)
{
    app.UseAuthentication();
}

app.UseAuthorization();
app.MapControllers();
app.Run();

static long ResolveTransportLimit(ConfigurationManager configuration)
{
    var megabytes = configuration.GetValue($"{FileStorageOptions.SectionName}:MaxFileSizeInMB", FileStorageOptionsValidator.DefaultMaxFileSizeInMb);
    if (megabytes is < FileStorageOptionsValidator.MinimumFileSizeInMb or > FileStorageOptionsValidator.MaximumFileSizeInMb)
    {
        megabytes = FileStorageOptionsValidator.DefaultMaxFileSizeInMb;
    }

    return (megabytes * 1024L * 1024L) + FileUploadLimits.MultipartOverheadBytes;
}
