using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AzureCliControlPanel.Core.Models;
using AzureCliControlPanel.Core.Services;

namespace AzureCliControlPanel.App.ViewModels;

public partial class SubscriptionViewModel : ObservableObject
{
    private readonly AzureCliFacade _cli;
    private readonly Func<Task> _afterSwitch;

    public string Id { get; }
    public string Name { get; }
    public string TenantId { get; }
    public string State { get; }

    [ObservableProperty] private bool _isActive;

    public string ActiveBadge => IsActive ? "ACTIVE" : string.Empty;
    public bool CanSwitch => !IsActive && State.Equals("Enabled", StringComparison.OrdinalIgnoreCase);

    public IAsyncRelayCommand SwitchCommand { get; }

    public SubscriptionViewModel(AzureSubscription s, bool isActive, AzureCliFacade cli, Func<Task> afterSwitch)
    {
        _cli = cli;
        _afterSwitch = afterSwitch;
        Id = s.Id;
        Name = s.Name;
        TenantId = s.TenantId;
        State = s.State;
        IsActive = isActive;

        SwitchCommand = new AsyncRelayCommand(SwitchAsync, () => CanSwitch);
    }

    public void UpdateIsActive(bool active)
    {
        IsActive = active;
        OnPropertyChanged(nameof(ActiveBadge));
        OnPropertyChanged(nameof(CanSwitch));
        SwitchCommand.NotifyCanExecuteChanged();
    }

    private async Task SwitchAsync()
    {
        await _cli.SwitchSubscriptionAsync(Id, CancellationToken.None);
        await _afterSwitch();
    }
}
