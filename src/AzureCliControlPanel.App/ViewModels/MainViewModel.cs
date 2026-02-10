using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AzureCliControlPanel.Core.Cli;
using AzureCliControlPanel.Core.Models;
using AzureCliControlPanel.Core.Services;
using AzureCliControlPanel.App.Windows;

namespace AzureCliControlPanel.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AzureCliFacade _cli;
    private readonly IOutputSink _sink;
    private readonly AzureCliContext _ctx;

    public ObservableCollection<SubscriptionViewModel> Subscriptions { get; } = new();
    public ObservableCollection<ResourceGroupNodeViewModel> ResourceGroups { get; } = new();
    public ObservableCollection<ResourceItemViewModel> Resources { get; } = new();
    public ObservableCollection<ResourceItemViewModel> FilteredResources { get; } = new();
    public ObservableCollection<OutputEntryViewModel> OutputEntries { get; } = new();

    public ObservableCollection<string> TypeFilters { get; } = new() { "All", "App Service", "Function App", "Static Web App", "Container App" };
    public ObservableCollection<string> ResourceGroupFilters { get; } = new() { "All" };

    [ObservableProperty] private SubscriptionViewModel? _selectedSubscription;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private string _selectedTypeFilter = "All";
    [ObservableProperty] private string _selectedResourceGroupFilter = "All";

    [ObservableProperty] private string _statusText = "Ready.";
    [ObservableProperty] private string _signedInText = "Not signed in.";
    [ObservableProperty] private string _azPathText = string.Empty;
    [ObservableProperty] private string _lastRefreshText = "Last refresh: (none)";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private OutputEntryViewModel? _selectedOutputEntry;

    public bool IsNotBusy => !IsBusy;

    public IAsyncRelayCommand RefreshAllCommand { get; }
    public IAsyncRelayCommand LoadSubscriptionsCommand { get; }
    public IAsyncRelayCommand LoadResourceGroupsCommand { get; }
    public IAsyncRelayCommand LoadResourcesCommand { get; }
    public IAsyncRelayCommand LoginCommand { get; }
    public IAsyncRelayCommand LogoutCommand { get; }
    public IRelayCommand ClearOutputCommand { get; }
    public IRelayCommand CopySelectedOutputCommand { get; }

    public MainViewModel(AzureCliFacade cli, IOutputSink sink, AzureCliContext ctx)
    {
        _cli = cli;
        _sink = sink;
        _ctx = ctx;

        AzPathText = "az: " + (_cli.AzPath ?? "(PATH)");

        if (sink is OutputSink os)
        {
            os.OnResult += r =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    OutputEntries.Insert(0, new OutputEntryViewModel(r));
                    if (OutputEntries.Count > 250) OutputEntries.RemoveAt(OutputEntries.Count - 1);
                });
            };
        }

        RefreshAllCommand = new AsyncRelayCommand(RefreshAllAsync, () => IsNotBusy);
        LoadSubscriptionsCommand = new AsyncRelayCommand(LoadSubscriptionsAsync, () => IsNotBusy);
        LoadResourceGroupsCommand = new AsyncRelayCommand(LoadResourceGroupsAsync, () => IsNotBusy);
        LoadResourcesCommand = new AsyncRelayCommand(LoadResourcesAsync, () => IsNotBusy);
        LoginCommand = new AsyncRelayCommand(LoginAsync, () => IsNotBusy);
        LogoutCommand = new AsyncRelayCommand(LogoutAsync, () => IsNotBusy);

        ClearOutputCommand = new RelayCommand(() => OutputEntries.Clear());
        CopySelectedOutputCommand = new RelayCommand(() =>
        {
            if (SelectedOutputEntry is null) return;
            var text =
                $"[{SelectedOutputEntry.Timestamp}] {SelectedOutputEntry.Command}\nExitCode={SelectedOutputEntry.ExitCode}\nDuration={SelectedOutputEntry.Duration}\n\nSTDERR:\n{SelectedOutputEntry.FullStdErr}\n\nSTDOUT:\n{SelectedOutputEntry.FullStdOut}";
            Clipboard.SetText(text);
        });

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FilterText) ||
                e.PropertyName == nameof(SelectedTypeFilter) ||
                e.PropertyName == nameof(SelectedResourceGroupFilter))
            {
                ApplyFilters();
            }
            if (e.PropertyName == nameof(IsBusy))
            {
                OnPropertyChanged(nameof(IsNotBusy));
                RefreshAllCommand.NotifyCanExecuteChanged();
                LoadSubscriptionsCommand.NotifyCanExecuteChanged();
                LoadResourceGroupsCommand.NotifyCanExecuteChanged();
                LoadResourcesCommand.NotifyCanExecuteChanged();
                LoginCommand.NotifyCanExecuteChanged();
                LogoutCommand.NotifyCanExecuteChanged();
            }
        };

        _ = RefreshAllAsync();
    }

    public void OnResourceGroupNodeSelected(ResourceGroupNodeViewModel node)
    {
        if (node.IsGroupNode && node.ResourceGroupName is not null)
            SelectedResourceGroupFilter = node.ResourceGroupName;
    }

    private async Task RefreshAllAsync()
    {
        await WithBusy(async ct =>
        {
            await RefreshAccountStatusAsync(ct);
            await LoadSubscriptionsAsync();
            await LoadResourceGroupsAsync();
            await LoadResourcesAsync();
            TouchRefreshTimestamp();
        }, "");
    }

    private async Task RefreshAccountStatusAsync(CancellationToken ct)
    {
        var (signedIn, account, error) = await _cli.GetAccountAsync(ct);
        if (!signedIn || account is null)
        {
            SignedInText = "Not signed in. Use Login.";
            StatusText = error ?? "Not signed in.";
            return;
        }

        SignedInText = $"Signed in as {account.UserName ?? "(unknown)"} ({account.UserType ?? "unknown"}) | Tenant {account.TenantId} | Sub {account.SubscriptionName}";
        StatusText = "Signed in.";
    }

    private async Task LoadSubscriptionsAsync()
    {
        await WithBusy(async ct =>
        {
            var (signedIn, account, _) = await _cli.GetAccountAsync(ct);
            if (!signedIn || account is null)
            {
                Application.Current.Dispatcher.Invoke(() => Subscriptions.Clear());
                return;
            }

            var subs = await _cli.ListSubscriptionsAsync(ct);

            Application.Current.Dispatcher.Invoke(() =>
            {
                Subscriptions.Clear();
                foreach (var s in subs.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var isActive = s.Id.Equals(account.SubscriptionId, StringComparison.OrdinalIgnoreCase);
                    Subscriptions.Add(new SubscriptionViewModel(s, isActive, _cli, async () =>
                    {
                        await RefreshAllAsync();
                    }));
                }

                SelectedSubscription = Subscriptions.FirstOrDefault(x => x.Id.Equals(account.SubscriptionId, StringComparison.OrdinalIgnoreCase));
            });
        }, "Loading subscriptions");
    }

    private async Task LoadResourceGroupsAsync()
    {
        await WithBusy(async ct =>
        {
            var (signedIn, _, _) = await _cli.GetAccountAsync(ct);
            if (!signedIn)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ResourceGroups.Clear();
                    ResourceGroupFilters.Clear();
                    ResourceGroupFilters.Add("All");
                    SelectedResourceGroupFilter = "All";
                });
                return;
            }

            var rgs = await _cli.ListResourceGroupsAsync(ct);
            Application.Current.Dispatcher.Invoke(() =>
            {
                ResourceGroups.Clear();
                ResourceGroupFilters.Clear();
                ResourceGroupFilters.Add("All");

                foreach (var rg in rgs)
                {
                    ResourceGroupFilters.Add(rg.Name);
                    ResourceGroups.Add(new ResourceGroupNodeViewModel(rg.Name, rg.Name, true));
                }

                if (!ResourceGroupFilters.Contains(SelectedResourceGroupFilter))
                    SelectedResourceGroupFilter = "All";
            });
        }, "Loading resource groups");
    }

    private async Task LoadResourcesAsync()
    {
        await WithBusy(async ct =>
        {
            var (signedIn, _, _) = await _cli.GetAccountAsync(ct);
            if (!signedIn)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Resources.Clear();
                    FilteredResources.Clear();
                });
                return;
            }

            var rgs = await _cli.ListResourceGroupsAsync(ct);
            var all = new List<AzureResource>();
            using var throttler = new SemaphoreSlim(_ctx.MaxConcurrency, _ctx.MaxConcurrency);

            var tasks = rgs.Select(async rg =>
            {
                await throttler.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var res = await _cli.ListResourcesInGroupAsync(rg.Name, ct).ConfigureAwait(false);
                    lock (all) all.AddRange(res);
                }
                finally
                {
                    throttler.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks).ConfigureAwait(false);

            Application.Current.Dispatcher.Invoke(() =>
            {
                Resources.Clear();
                foreach (var r in all.OrderBy(x => x.ResourceGroup, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    Resources.Add(new ResourceItemViewModel(r, _cli, ShowLogTailAsync, ShowTextDetailsAsync));
                }
            });

            using var throttler2 = new SemaphoreSlim(_ctx.MaxConcurrency, _ctx.MaxConcurrency);
            var refreshTasks = Resources.Select(async rvm =>
            {
                await throttler2.WaitAsync(ct).ConfigureAwait(false);
                try { await rvm.RefreshAsync(ct).ConfigureAwait(false); }
                catch { }
                finally { throttler2.Release(); }
            }).ToList();

            await Task.WhenAll(refreshTasks).ConfigureAwait(false);

            Application.Current.Dispatcher.Invoke(ApplyFilters);
        }, "Loading resources");
    }

    private void ApplyFilters()
    {
        var txt = (FilterText ?? string.Empty).Trim();
        var rgFilter = SelectedResourceGroupFilter ?? "All";
        var typeFilter = SelectedTypeFilter ?? "All";

        bool matches(ResourceItemViewModel r)
        {
            if (rgFilter != "All" && !r.ResourceGroup.Equals(rgFilter, StringComparison.OrdinalIgnoreCase))
                return false;

            if (typeFilter != "All" && !r.DisplayType.Equals(typeFilter, StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.IsNullOrWhiteSpace(txt))
                return true;

            return r.Name.Contains(txt, StringComparison.OrdinalIgnoreCase)
                   || r.ResourceGroup.Contains(txt, StringComparison.OrdinalIgnoreCase)
                   || r.DisplayType.Contains(txt, StringComparison.OrdinalIgnoreCase);
        }

        FilteredResources.Clear();
        foreach (var r in Resources.Where(matches))
            FilteredResources.Add(r);
    }

    private async Task LoginAsync() => await WithBusy(async ct =>
    {
        await _cli.LoginAsync(ct);
        await RefreshAllAsync();
    }, "Login");

    private async Task LogoutAsync() => await WithBusy(async ct =>
    {
        await _cli.LogoutAsync(ct);
        await RefreshAllAsync();
    }, "Logout");

    private void TouchRefreshTimestamp()
        => LastRefreshText = "Last refresh: " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");

    private async Task ShowLogTailAsync(string rg, string name)
    {
        var res = Resources.FirstOrDefault(r => r.ResourceGroup.Equals(rg, StringComparison.OrdinalIgnoreCase) && r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (res is null) return;

        var window = new LogTailWindow { Owner = Application.Current.MainWindow };
        var cts = new CancellationTokenSource();
        var vm = new LogTailViewModel(window, $"{res.DisplayType}: {name} ({rg})", cts);
        window.DataContext = vm;
        window.Show();

        try
        {
            IAsyncEnumerable<string> stream = res.IsWebApp
                ? await _cli.WebAppLogTailAsync(rg, name, cts.Token)
                : await _cli.ContainerAppLogsAsync(rg, name, cts.Token);

            await foreach (var line in stream.WithCancellation(cts.Token))
            {
                var redacted = Redaction.Redact(line);
                Application.Current.Dispatcher.Invoke(() => vm.AppendLine(redacted));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Application.Current.Dispatcher.Invoke(() => vm.AppendLine("[error] " + ex.Message));
        }
    }

    private Task ShowTextDetailsAsync(string title, string subtitle, string body)
    {
        return Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var w = new TextDetailsWindow { Owner = Application.Current.MainWindow };
            var vm = new TextDetailsViewModel(w, $"{title} - {subtitle}", body);
            w.DataContext = vm;
            w.ShowDialog();
        }).Task;
    }

    private async Task WithBusy(Func<CancellationToken, Task> action, string label)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = label + "...";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await action(cts.Token);
            StatusText = "Ready.";
            TouchRefreshTimestamp();
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
