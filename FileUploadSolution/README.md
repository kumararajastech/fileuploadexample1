# File Upload API

ASP.NET Core Web API on .NET 10 that accepts a multipart upload and streams it into a configured local folder or Windows UNC share.

The controller validates the request. `SharedFolderStorageService` writes the stream. The response contains a server-generated file id and never contains the physical storage path.

## Run locally

Requires the .NET 10 SDK.

```powershell
cd C:\Corebridge\FileUploadSolution
dotnet restore FileUploadSolution.slnx
dotnet build FileUploadSolution.slnx
dotnet test FileUploadSolution.slnx
dotnet run --project FileUpload.Api
```

`dotnet run` uses the Development profile in `Properties/launchSettings.json`:

- HTTPS: `https://localhost:5001`
- HTTP: `http://localhost:5000`
- Storage folder: `C:\Corebridge\FileUploadSolution\storage`
- Authentication: disabled only because the environment is Development and `Security:RequireAuthentication` is `false`

The application does not create the storage folder. Create it before uploading:

```powershell
New-Item -ItemType Directory -Force -Path C:\Corebridge\FileUploadSolution\storage
```

Swagger UI is available in Development at `https://localhost:5001/swagger`. The OpenAPI document is `https://localhost:5001/swagger/v1/swagger.json`.

Outside Development, `Security:RequireAuthentication` must be `true`. The API then uses Windows negotiate authentication and requires an authenticated caller. Replace that registration in `Program.cs` with the organization's identity provider if Windows authentication is not the provider you use. Do not invent a local username/password scheme or embed secrets in source.

## Configuration

`FileStorage` is bound to `FileStorageOptions` and validated when the host starts.

Local folder:

```json
{
  "FileStorage": {
    "SharedFolderPath": "D:\\Departments\\Uploads",
    "MaxFileSizeInMB": 20,
    "AllowedExtensions": [ ".pdf", ".docx", ".xlsx", ".csv", ".txt", ".png", ".jpg", ".jpeg" ]
  }
}
```

UNC share:

```json
{
  "FileStorage": {
    "SharedFolderPath": "\\\\FileServer\\SharedFiles",
    "MaxFileSizeInMB": 20,
    "AllowedExtensions": [ ".pdf", ".docx", ".xlsx", ".csv", ".txt", ".png", ".jpg", ".jpeg" ]
  }
}
```

Prefer environment variables for the machine-specific path so the same build can be deployed without editing files:

```powershell
$env:FileStorage__SharedFolderPath = "\\FileServer\SharedFiles"
$env:FileStorage__MaxFileSizeInMB = "20"
```

Do not put domain passwords, service-account passwords, or connection strings with credentials in `appsettings.json`. The process identity is what accesses the share.

`MaxFileSizeInMB` must be from 1 through 512. Extensions must be a leading dot plus 1 to 10 letters or digits. Executable and script extensions such as `.exe`, `.dll`, `.bat`, `.ps1`, and `.js` are rejected. Changing the configured size requires a process restart so the HTTP request limit matches the new value.

An empty `SharedFolderPath` fails startup. A path that is syntactically valid but missing or unreachable lets the process start, is logged by the startup check, and causes upload requests to fail. The API does not create the folder.

## Upload request

Development, with authentication disabled:

```bash
curl -X POST "https://localhost:5001/api/files/upload" \
  -H "accept: application/json" \
  -F "file=@document.pdf"
```

Production, with Windows authentication:

```bash
curl --negotiate -u : -X POST "https://files.example.com/api/files/upload" \
  -H "accept: application/json" \
  -F "file=@document.pdf"
```

The form field name is `file`.

### Postman

- Method: `POST`
- URL: `https://localhost:5001/api/files/upload`
- Body: `form-data`
- Key: `file` (type File)
- Value: the file to upload
- Header: `Accept: application/json`
- Authorization: None in Development. For a production IIS site using Windows authentication, use NTLM/Negotiate authentication.

### Success

`201 Created`

```json
{
  "fileId": "a-guid-value",
  "fileName": "document.pdf",
  "fileSizeInBytes": 125000,
  "contentType": "application/pdf",
  "uploadedAtUtc": "2026-10-19T16:00:00+00:00"
}
```

`fileName` is a sanitized display name. The stored object is named `{fileId}{extension}`. `contentType` is chosen from the validated extension. The client content type is ignored. `fileId` is a GUID.

### Errors

```json
{
  "title": "Bad Request",
  "status": 400,
  "detail": "A file is required.",
  "errorCode": "file_required",
  "traceId": "0HN..."
}
```

| Status | When | Error code |
| --- | --- | --- |
| 400 | Missing file, empty file, or extension not allowed | `file_required`, `file_empty`, `file_extension_not_allowed` |
| 401 | Authentication is required and missing or invalid | `unauthorized` |
| 403 | The authenticated caller is not authorized | `forbidden` |
| 413 | The file or multipart body exceeds the configured limit | `file_too_large` |
| 500 | Unexpected failure or invalid storage configuration | `unexpected_error`, `storage_failure` |
| 503 | The share is unreachable or the process identity was denied access | `storage_unavailable` |

Error bodies do not include stack traces, exception messages, or filesystem paths. The server log records the failure category, file id, trace id, and storage root.

## Service lifetime

`IFileStorageService`, the file-name factory, and the file-system accessor are singletons. They keep no per-request state. Each upload uses its own stream and a new GUID file name, so concurrent requests do not share mutable upload state. `IOptionsMonitor<FileStorageOptions>` and `ILogger<T>` are safe to hold for the process lifetime.

## Deploy on IIS

1. Install the .NET 10 Hosting Bundle on the Windows server and restart IIS (`net stop was /y` then `net start w3svc`).
2. Publish a framework-dependent build:

   ```powershell
   dotnet publish FileUpload.Api\FileUpload.Api.csproj -c Release -o C:\inetpub\fileupload
   ```

3. Create an IIS site or application whose physical path is `C:\inetpub\fileupload`. Use an application pool with **No Managed Code** and the integrated pipeline.
4. Set the pool identity to a domain service account or a group managed service account. Do not use a copied password in configuration.
5. Set `FileStorage__SharedFolderPath` in the IIS configuration for that site, or in `web.config` environment variables on the server. Set `ASPNETCORE_ENVIRONMENT` to `Production`.
6. In IIS Manager, enable Windows Authentication for the site. Leave Anonymous Authentication enabled only if ASP.NET Core should be the component that returns `401`. With the production fallback policy, anonymous requests are rejected by the API.
7. Confirm `web.config` `maxAllowedContentLength` stays at or above the configured upload size plus multipart overhead. The checked-in ceiling is 512 MB plus 1 MB. Lower it to match the organization's real limit.
8. Do not enable a virtual directory or static-file handler for the shared folder. This API does not call `UseStaticFiles`, and it does not provide a download endpoint.
9. Swagger is not enabled outside Development.

`web.config` is included for the ASP.NET Core Module. Windows Authentication is configured in IIS Manager because the authentication section is often locked in `applicationHost.config`.

## Shared-folder permissions

### UNC path

Use a path such as `\\FileServer\SharedFiles`. The file server must be reachable from the IIS server by DNS name, TCP 445 must be allowed, and the share must exist. The API does not map a drive and does not store credentials.

Grant two layers of access to the application-pool identity:

1. **Share** permission: Change. Do not grant Full Control to broad groups.
2. **NTFS** permission on the folder: Modify, including list, create files, and write data. Apply to the folder and files it will contain. Do not grant permissions to Everyone.

A mapped drive such as `Z:\SharedFiles` is unreliable for IIS. Mapped drives belong to an interactive logon session. The application pool runs in session 0 and does not see a user's mapped letters. After a reboot the mapping may not exist. Configure a UNC path.

### Identity

Use a domain service account or a gMSA as the pool identity. A gMSA avoids a stored password. `ApplicationPoolIdentity` (`IIS APPPOOL\AppPoolName`) is a virtual account on the IIS machine. A remote file server cannot grant that virtual account access unless you instead grant the IIS computer account (`DOMAIN\IISSERVER$`), which is broader than a dedicated service account. A dedicated domain account or gMSA is the better choice for a remote share.

The account should be able to create and write files in that folder and should not be an administrator. Do not grant Execute to users who can browse the share. Do not place the share under an IIS site root.

### Test write access as the application identity

On the IIS server, as an administrator:

```powershell
$share = "\\FileServer\SharedFiles"
$probe = Join-Path $share ("write-test-" + [guid]::NewGuid().ToString("N") + ".tmp")

# Run the write in the same identity the pool uses. For a domain account, start an elevated
# PowerShell window as that account, then run:
New-Item -ItemType File -Path $probe -Value "ok" | Out-Null
Get-Item $probe | Select-Object FullName, Length
Remove-Item $probe
```

A successful create and delete shows that both the share permission and the NTFS permission allow the identity to write. If this fails, the API will fail too.

You can also set the pool identity, recycle the pool, and upload a small `.txt` file with `curl --negotiate -u :`. A `201` response confirms the path used by the site. A `503` means the share could not be reached or the identity was denied; the site log contains the category and the storage root.

### Diagnose access denied and network path errors

Check the application log for `FileStorageFailureReason` and the Windows error inside the logged exception.

| Windows code | Meaning | What to check |
| --- | --- | --- |
| 5 (`0x80070005`) | Access denied | Share permission and NTFS permission for the pool identity. Confirm the request is running as that identity, not your interactive user. |
| 1326 | Logon failure | The service account cannot authenticate to the file server. For a gMSA, confirm the IIS host is allowed to retrieve the managed password. |
| 53 (`0x80070035`) | Network path not found | Server name, DNS, firewall TCP 445, or the server is offline. |
| 67 (`0x80070043`) | Network name not found | The share name is wrong or the share was not created. |
| 3 | Path not found | The folder under the share does not exist. Create it manually. The API will not create it. |

Useful checks from the IIS server:

```powershell
Test-NetConnection FileServer -Port 445
Test-Path "\\FileServer\SharedFiles"
icacls "\\FileServer\SharedFiles"
```

`Test-Path` uses the account running the command. Run it as the pool identity before treating a successful result as proof that IIS can write.

## Security requirements that remain operational

This API does not scan uploads for malware. Before any other user or system can open a stored file, scan it with the organization's malware scanner and quarantine failures. Extension checks are not content inspection.

Also keep these deployment controls:

- The storage identity has the minimum share and NTFS rights required to create and write files.
- Uploaded files are not executable from the share and are not served by static-file middleware.
- Authentication stays enabled outside Development and uses the organization's identity provider.
- Request size is limited by IIS `maxAllowedContentLength`, the Kestrel request limit, the multipart form limit, and the application check against `MaxFileSizeInMB`.
- Client file names are not used as destination paths. Stored names are GUIDs plus the validated extension.
- Logs omit file contents, passwords, and authentication tokens. They include the trace id, file id, size, and failure category.
