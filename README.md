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

The development dashboard password for signing in is:

```text
local-dashboard-password
```

To generate a new dashboard password hash:

```powershell
dotnet run --project .\src\Openclaw.Dashboard\Openclaw.Dashboard.csproj -- --generate-auth-hash '<new dashboard password>'
```

Then set the generated value with configuration outside source control for production:

```powershell
$env:DashboardAuth__PasswordHash = '<generated hash>'
```

## Production Run

Put Cloudflare Access in front of the app and restrict access to trusted users. The app still requires its own admin token for every dashboard write action, including setting changes and signal reviews.

Set a strong token outside source control:

```powershell
$env:ProductionSecurity__AdminToken = '<strong random token>'
$env:DashboardAuth__PasswordHash = '<generated dashboard password hash>'
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

## Mobile Access

For short-lived mobile testing without a domain, install `cloudflared`, run the dashboard on localhost, and start a quick tunnel:

```powershell
dotnet run --project .\src\Openclaw.Dashboard\Openclaw.Dashboard.csproj --urls http://127.0.0.1:5077
cloudflared tunnel --url http://127.0.0.1:5077
```

Open the generated `https://*.trycloudflare.com` URL on your phone and sign in with the dashboard password. Treat quick tunnel URLs as temporary testing links.

For stable mobile access later, add a domain to Cloudflare, create a named Cloudflare Tunnel, route a hostname such as `dashboard.example.com` to `http://127.0.0.1:5077`, and protect that hostname with a Cloudflare Access self-hosted application using email one-time PIN or your preferred identity provider.

## Time Zones

The dashboard treats `signals.db` timestamps as UTC at rest and displays signal times in Eastern time. During daylight saving time this renders as EDT; otherwise it renders as EST. Signal date filters are interpreted as Eastern calendar days and converted back to UTC for SQLite queries.

The display timezone is configurable:

```json
"DashboardTime": {
  "TimeZoneId": "Eastern Standard Time"
}
```
