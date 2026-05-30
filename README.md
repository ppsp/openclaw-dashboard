# Openclaw Dashboard

A .NET 10 Blazor Web App dashboard shell for Openclaw Mission Control.

## Run Locally

The dashboard uses Blazor Web App with Interactive Server rendering and MudBlazor.

Openclaw paths are configured in `src/Openclaw.Dashboard/appsettings.json`:

```json
"Openclaw": {
  "RootPath": "C:\\Users\\User\\.openclaw",
  "WorkspacePath": "C:\\Users\\User\\.openclaw\\workspace",
  "CronPath": "C:\\Users\\User\\.openclaw\\cron",
  "SqlitePath": "C:\\Users\\User\\.openclaw\\workspace\\sqlite"
}
```

```powershell
dotnet restore
dotnet build
dotnet run --project .\src\Openclaw.Dashboard\Openclaw.Dashboard.csproj
```

If `dotnet` is not on PATH immediately after installing the SDK, use:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build
& 'C:\Program Files\dotnet\dotnet.exe' run --project .\src\Openclaw.Dashboard\Openclaw.Dashboard.csproj
```

Then open the local URL printed by `dotnet run`.

The development admin token for write actions is:

```text
local-admin-token
```

## Production Run

Put Cloudflare Access in front of the app and restrict access to trusted users. The app still requires its own admin token for every dashboard write action, including setting changes and signal reviews.

Set a strong token outside source control:

```powershell
$env:ProductionSecurity__AdminToken = '<strong random token>'
dotnet run --project .\src\Openclaw.Dashboard\Openclaw.Dashboard.csproj --urls http://127.0.0.1:5077
```

If the app runs behind a reverse proxy, explicitly configure which forwarded headers are accepted:

```json
"ProductionSecurity": {
  "AllowedForwardedHeaders": [ "XForwardedFor", "XForwardedProto", "XForwardedHost" ],
  "KnownProxies": [ "127.0.0.1" ],
  "KnownNetworks": []
}
```

Use Cloudflare Access for identity and network exposure, keep the app bound to a private interface or localhost behind the tunnel/proxy, and leave `ProductionSecurity:AdminToken` empty only when you intentionally want all write actions disabled.
