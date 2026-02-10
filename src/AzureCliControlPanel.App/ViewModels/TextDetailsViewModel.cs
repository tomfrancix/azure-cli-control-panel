using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AzureCliControlPanel.App.ViewModels;

public partial class TextDetailsViewModel : ObservableObject
{
    [ObservableProperty] private string _titleText = "Details";
    [ObservableProperty] private string _bodyText = string.Empty;

    public IRelayCommand CopyCommand { get; }
    public IRelayCommand CloseCommand { get; }

    private readonly Window _window;

    public TextDetailsViewModel(Window window, string title, string body)
    {
        _window = window;
        TitleText = title;
        BodyText = body;

        CopyCommand = new RelayCommand(() => Clipboard.SetText(BodyText ?? string.Empty));
        CloseCommand = new RelayCommand(() => _window.Close());
    }
}
