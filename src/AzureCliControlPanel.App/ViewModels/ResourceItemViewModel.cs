using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AzureCliControlPanel.Core.Models;
using AzureCliControlPanel.Core.Services;

namespace AzureCliControlPanel.App.ViewModels;

public partial class ResourceItemViewModel : ObservableObject
{
    private readonly AzureCliFacade _cli;
    private readonly Func<string, string, Task> _showLogTail;
    private readonly Func<string, string, string, Task> _showTextDetails;

    public string Id { get; }
    public string Name { get; }
    public string Type { get; }
    public string? Kind { get; }
    public string ResourceGroup { get; }

    [ObservableProperty] private string _stateText = "Unknown";
    [ObservableProperty] private string _identityTenantId = string.Empty;
    [ObservableProperty] private string _identityClientId = "N/A";
    [ObservableProperty] private string _identitySource = "N/A";
    [ObservableProperty] private bool _isBusy;

    private AppIdentityInfo? _identityRaw;

    public string DisplayType =>
        Type.Equals("Microsoft.Web/sites", StringComparison.OrdinalIgnoreCase) && (Kind?.Contains("functionapp", StringComparison.OrdinalIgnoreCase) == true)
            ? "Function App"
            : Type.Equals("Microsoft.Web/sites", StringComparison.OrdinalIgnoreCase)
                ? "App Service"
                : Type.Equals("Microsoft.Web/staticSites", StringComparison.OrdinalIgnoreCase)
                    ? "Static Web App"
                    : Type.Equals("Microsoft.App/containerApps", StringComparison.OrdinalIgnoreCase)
                        ? "Container App"
                        : Type;

    public bool IsWebApp => Type.Equals("Microsoft.Web/sites", StringComparison.OrdinalIgnoreCase);
    public bool IsContainerApp => Type.Equals("Microsoft.App/containerApps", StringComparison.OrdinalIgnoreCase);

    public bool CanStart => IsWebApp && !IsBusy && StateText.Equals("Stopped", StringComparison.OrdinalIgnoreCase);
    public bool CanStop => IsWebApp && !IsBusy && StateText.Equals("Running", StringComparison.OrdinalIgnoreCase);
    public bool CanRestart => (IsWebApp || IsContainerApp) && !IsBusy;
    public bool CanLogs => (IsWebApp || IsContainerApp) && !IsBusy;
    public bool CanLogConfig => IsWebApp && !IsBusy;
    public bool CanKudu => IsWebApp && !IsBusy;

    public IAsyncRelayCommand StartCommand { get; }
    public IAsyncRelayCommand StopCommand { get; }
    public IAsyncRelayCommand RestartCommand { get; }
    public IAsyncRelayCommand LogsCommand { get; }
    public IAsyncRelayCommand LogConfigCommand { get; }
    public IAsyncRelayCommand PortalCommand { get; }
    public IRelayCommand KuduCommand { get; }
    public IAsyncRelayCommand IdentityDetailsCommand { get; }

    public ResourceItemViewModel(AzureResource r, AzureCliFacade cli,
        Func<string, string, Task> showLogTail,
        Func<string, string, string, Task> showTextDetails)
    {
        _cli = cli;
        _showLogTail = showLogTail;
        _showTextDetails = showTextDetails;

        Id = r.Id;
        Name = r.Name;
        Type = r.Type;
        Kind = r.Kind;
        ResourceGroup = r.ResourceGroup;

        StartCommand = new AsyncRelayCommand(StartAsync, () => CanStart);
        StopCommand = new AsyncRelayCommand(StopAsync, () => CanStop);
        RestartCommand = new AsyncRelayCommand(RestartAsync, () => CanRestart);
        LogsCommand = new AsyncRelayCommand(LogsAsync, () => CanLogs);
        LogConfigCommand = new AsyncRelayCommand(LogConfigAsync, () => CanLogConfig);
        PortalCommand = new AsyncRelayCommand(OpenPortalAsync);
        KuduCommand = new RelayCommand(OpenKudu, () => CanKudu);
        IdentityDetailsCommand = new AsyncRelayCommand(ShowIdentityDetailsAsync);
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            if (IsWebApp)
            {
                var rt = await _cli.GetWebAppRuntimeAsync(ResourceGroup, Name, ct);
                StateText = rt.State.ToString();
                var ident = await _cli.GetWebAppIdentityAsync(ResourceGroup, Name, ct);
                ApplyIdentity(ident);
            }
            else if (IsContainerApp)
            {
                var rt = await _cli.GetContainerAppRuntimeAsync(ResourceGroup, Name, ct);
                StateText = rt.State.ToString();
                IdentitySource = "N/A (not implemented for Container Apps)";
                IdentityClientId = "N/A";
                IdentityTenantId = string.Empty;
            }
            else
            {
                StateText = "Unknown";
            }

            NotifyActionCanExecute();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyIdentity(AppIdentityInfo info)
    {
        _identityRaw = info;
        IdentityTenantId = info.TenantId;
        IdentityClientId = info.ClientId ?? "N/A";
        IdentitySource = info.Source;
    }

    private async Task StartAsync() => await WithBusy(async ct =>
    {
        await _cli.WebAppStartAsync(ResourceGroup, Name, ct);
        await RefreshAsync(ct);
    });

    private async Task StopAsync() => await WithBusy(async ct =>
    {
        await _cli.WebAppStopAsync(ResourceGroup, Name, ct);
        await RefreshAsync(ct);
    });

    private async Task RestartAsync() => await WithBusy(async ct =>
    {
        if (IsWebApp) await _cli.WebAppRestartAsync(ResourceGroup, Name, ct);
        else if (IsContainerApp) await _cli.ContainerAppRestartAsync(ResourceGroup, Name, ct);
        await RefreshAsync(ct);
    });

    private async Task LogsAsync() => await _showLogTail(ResourceGroup, Name);

    private async Task LogConfigAsync()
    {
        await WithBusy(async ct =>
        {
            var output = await _cli.WebAppLogConfigAsync(ResourceGroup, Name, "Information", ct);
            await _showTextDetails("Log Config", $"{Name} ({ResourceGroup})", output);
            await RefreshAsync(ct);
        });
    }

    private async Task OpenPortalAsync()
    {
        var url = await _cli.BuildPortalLinkAsync(Id, CancellationToken.None);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OpenKudu()
    {
        var url = $"https://{Name}.scm.azurewebsites.net/";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async Task ShowIdentityDetailsAsync()
    {
        if (_identityRaw is null)
        {
            await _showTextDetails("Identity", $"{Name} ({ResourceGroup})", "No identity data loaded yet. Reload resources first.");
            return;
        }

        var lines = new List<string>
        {
            $"Source: {_identityRaw.Source}",
            $"TenantId: {_identityRaw.TenantId}",
            $"ClientId: {_identityRaw.ClientId ?? "N/A"}",
            $"Managed Identity PrincipalId: {_identityRaw.ManagedIdentityPrincipalId ?? "N/A"}",
            "",
            "Raw fields:"
        };
        foreach (var kv in _identityRaw.Raw.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            lines.Add($"{kv.Key}: {kv.Value}");

        await _showTextDetails("Identity Details", $"{Name} ({ResourceGroup})", string.Join(Environment.NewLine, lines));
    }

    private async Task WithBusy(Func<CancellationToken, Task> action)
    {
        var prev = IsBusy;
        IsBusy = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await action(cts.Token);
        }
        finally
        {
            IsBusy = prev;
            NotifyActionCanExecute();
        }
    }

    private void NotifyActionCanExecute()
    {
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();
        LogsCommand.NotifyCanExecuteChanged();
        LogConfigCommand.NotifyCanExecuteChanged();
        PortalCommand.NotifyCanExecuteChanged();
        KuduCommand.NotifyCanExecuteChanged();
        IdentityDetailsCommand.NotifyCanExecuteChanged();
    }
}
