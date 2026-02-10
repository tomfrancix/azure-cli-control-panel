using AzureCliControlPanel.Core.Cli;

namespace AzureCliControlPanel.App.ViewModels;

public sealed class OutputEntryViewModel
{
    public string Timestamp { get; }
    public string Command { get; }
    public int ExitCode { get; }
    public string Duration { get; }
    public string Summary { get; }
    public string FullStdOut { get; }
    public string FullStdErr { get; }

    public OutputEntryViewModel(AzResult r)
    {
        Timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
        Command = r.Command.ToString();
        ExitCode = r.ExitCode;
        Duration = r.Duration.TotalSeconds.ToString("0.000") + "s";

        var stdOut = Redaction.Redact((r.StdOut ?? string.Empty).Trim());
        var stdErr = Redaction.Redact((r.StdErr ?? string.Empty).Trim());
        FullStdOut = stdOut;
        FullStdErr = stdErr;

        var combined = (stdErr + "\n" + stdOut).Trim();
        Summary = combined.Length > 500 ? combined.Substring(0, 500) + " ..." : combined;
    }
}
