namespace AzureCliControlPanel.Core.Cli;

public sealed record AzResult(
    AzCommand Command,
    int ExitCode,
    string StdOut,
    string StdErr,
    TimeSpan Duration
)
{
    public bool IsSuccess => ExitCode == 0;
}
