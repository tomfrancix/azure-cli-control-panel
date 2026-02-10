# Azure CLI Control Panel (WPF)

Windows desktop GUI wrapping **Azure CLI (`az`)** so you can switch subscriptions, browse resource groups, and control app-like resources without living in a terminal.

## Features

- Subscriptions: list + highlight active + one-click switch.
- Resource Groups: list.
- Resources (app-like): App Service/Function Apps, Static Web Apps, Container Apps.
- Identity (best-effort & honest):
  - Managed identity principalId (system-assigned)
  - EasyAuth/Entra clientId + tenant inferred from issuer (when configured)
  - Shows **N/A** if not configured.
- Actions:
  - Web apps: start/stop/restart, log tail, enable filesystem logging.
  - Container apps: logs follow, restart latest revision (best-effort).
  - Portal + Kudu quick links.
- Output panel: every `az` command, exit code, duration, redacted output.

## Prerequisites

- Windows 10/11
- .NET 8 SDK
- Azure CLI installed (`az --version`)

## Run

```powershell
dotnet restore
dotnet build
dotnet run --project .\src\AzureCliControlPanel.App\AzureCliControlPanel.App.csproj
```

## Notes

Identity mapping depends on your Azure CLI version and whether EasyAuth is configured. The UI will not guess.
"# azure-cli-control-panel" 
