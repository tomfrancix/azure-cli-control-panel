namespace AzureCliControlPanel.Core.Models;

public sealed record AzureResource(
    string Id,
    string Name,
    string Type,
    string? Kind,
    string ResourceGroup,
    string Location
);
