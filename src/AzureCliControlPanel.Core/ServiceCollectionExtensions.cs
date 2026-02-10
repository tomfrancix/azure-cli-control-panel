using AzureCliControlPanel.Core.Cli;
using AzureCliControlPanel.Core.Services;
using AzureCliControlPanel.Core.Util;
using Microsoft.Extensions.DependencyInjection;

namespace AzureCliControlPanel.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAzureCliControlPanelCore(this IServiceCollection services, Action<AzureCliContext>? configure = null)
    {
        var ctx = new AzureCliContext();
        configure?.Invoke(ctx);

        services.AddSingleton(ctx);
        services.AddSingleton<SimpleMemoryCache>();
        services.AddSingleton<IOutputSink, OutputSink>();
        services.AddSingleton<IAzCliRunner>(sp => new AzCliRunner(sp.GetRequiredService<AzureCliContext>().AzPathOverride));
        services.AddSingleton<AzureCliFacade>();

        return services;
    }
}
