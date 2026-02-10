using System.Windows;
using AzureCliControlPanel.Core;
using AzureCliControlPanel.Core.Services;
using AzureCliControlPanel.App.ViewModels;
using AzureCliControlPanel.App.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AzureCliControlPanel.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var sc = new ServiceCollection();
        sc.AddLogging(lb => lb.AddConsole());

        sc.AddAzureCliControlPanelCore(ctx =>
        {
            ctx.CacheTtl = TimeSpan.FromSeconds(45);
            ctx.MaxConcurrency = 4;
        });

        sc.AddSingleton<MainViewModel>();
        sc.AddSingleton<MainWindow>();

        _services = sc.BuildServiceProvider();

        var window = _services.GetRequiredService<MainWindow>();
        window.DataContext = _services.GetRequiredService<MainViewModel>();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
