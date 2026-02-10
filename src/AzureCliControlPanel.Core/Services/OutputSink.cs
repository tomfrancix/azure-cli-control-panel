using AzureCliControlPanel.Core.Cli;

namespace AzureCliControlPanel.Core.Services;

public interface IOutputSink
{
    void Publish(AzResult result);
    void PublishText(string text);
}

public sealed class OutputSink : IOutputSink
{
    private readonly object _lock = new();
    public event Action<AzResult>? OnResult;
    public event Action<string>? OnText;

    public void Publish(AzResult result)
    {
        lock (_lock) { OnResult?.Invoke(result); }
    }

    public void PublishText(string text)
    {
        lock (_lock) { OnText?.Invoke(text); }
    }
}
