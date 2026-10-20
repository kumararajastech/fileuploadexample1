namespace FileUpload.Api.Options;

/// <summary>
/// Authentication settings for the file upload API.
/// </summary>
/// <remarks>
/// Outside Development, authentication cannot be disabled. Production uses the configured
/// identity provider. This application enables Windows negotiate authentication for IIS and
/// does not embed credentials.
/// </remarks>
public sealed class SecurityOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "Security";

    /// <summary>
    /// Gets or sets a value indicating whether callers must be authenticated.
    /// </summary>
    public bool RequireAuthentication { get; set; } = true;
}
