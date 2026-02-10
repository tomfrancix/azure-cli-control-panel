using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AzureCliControlPanel.App.ViewModels;

public partial class LogTailViewModel : ObservableObject
{
    [ObservableProperty] private string _titleText = "Log Tail";
    [ObservableProperty] private string _logText = string.Empty;

    public IRelayCommand StopCommand { get; }
    public IRelayCommand CopyAllCommand { get; }

    private readonly CancellationTokenSource _cts;
    private readonly Window _window;

    public LogTailViewModel(Window window, string title, CancellationTokenSource cts)
    {
        _window = window;
        _cts = cts;
        TitleText = title;

        StopCommand = new RelayCommand(() =>
        {
            if (!_cts.IsCancellationRequested) _cts.Cancel();
            _window.Close();
        });

        CopyAllCommand = new RelayCommand(() => Clipboard.SetText(LogText ?? string.Empty));
    }

    public void AppendLine(string line)
    {
        const int maxChars = 200_000;
        var current = LogText ?? string.Empty;
        var next = current + line + Environment.NewLine;

        if (next.Length > maxChars)
            next = next.Substring(next.Length - maxChars);

        LogText = next;
    }
}
