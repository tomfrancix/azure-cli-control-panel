namespace AzureCliControlPanel.Core.Models;

public sealed record AzureAccount(
    string? UserName,
    string? UserType,
    string SubscriptionId,
    string SubscriptionName,
    string TenantId,
    string? EnvironmentName
);
