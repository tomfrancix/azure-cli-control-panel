using CommunityToolkit.Mvvm.ComponentModel;

namespace AzureCliControlPanel.App.ViewModels;

public partial class ResourceGroupNodeViewModel : ObservableObject
{
    public string DisplayName { get; }
    public string? ResourceGroupName { get; }
    public bool IsGroupNode { get; }
    public List<ResourceGroupNodeViewModel> Children { get; } = new();

    public ResourceGroupNodeViewModel(string displayName, string? resourceGroupName, bool isGroupNode)
    {
        DisplayName = displayName;
        ResourceGroupName = resourceGroupName;
        IsGroupNode = isGroupNode;
    }
}
