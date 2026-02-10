namespace AzureCliControlPanel.Core.Services;

public sealed class AzureCliContext
{
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromSeconds(45);
    public int MaxConcurrency { get; set; } = 4;
    public string? AzPathOverride { get; set; }
    public string? HealthCheckPath { get; set; } = "/health";
}
