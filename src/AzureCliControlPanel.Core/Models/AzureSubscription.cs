namespace AzureCliControlPanel.Core.Models;

public sealed record AzureSubscription(
    string Id,
    string Name,
    string TenantId,
    string State,
    bool IsDefault
);
