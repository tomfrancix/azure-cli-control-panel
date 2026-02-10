namespace AzureCliControlPanel.Core.Models;

public enum AppRuntimeState
{
    Unknown = 0,
    Running = 1,
    Stopped = 2,
}

public sealed record AppRuntimeInfo(
    AppRuntimeState State,
    IReadOnlyList<string> HostNames
);
