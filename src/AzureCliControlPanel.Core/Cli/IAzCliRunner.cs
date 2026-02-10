namespace AzureCliControlPanel.Core.Cli;

public interface IAzCliRunner
{
    Task<AzResult> RunAsync(AzCommand command, CancellationToken cancellationToken);
    Task<IAsyncEnumerable<string>> RunStreamingAsync(AzCommand command, CancellationToken cancellationToken);
    string? AzPath { get; }
}
