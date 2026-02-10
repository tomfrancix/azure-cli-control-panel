namespace AzureCliControlPanel.Core.Models;

public sealed record AppIdentityInfo(
    string TenantId,
    string? ClientId,
    string? ManagedIdentityPrincipalId,
    string Source,
    IReadOnlyDictionary<string, string> Raw
);
