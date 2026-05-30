# Openclaw Dashboard

A .NET 10 Blazor Web App dashboard shell for Openclaw Mission Control.

## Run Locally

The current shell uses Blazor Web App with Interactive Server rendering and MudBlazor. No database logic is wired yet.

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
