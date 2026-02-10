using AzureCliControlPanel.Core.Cli;
using FluentAssertions;

namespace AzureCliControlPanel.Tests;

public sealed class AzCommandTests
{
    [Fact]
    public void ToString_ShouldRenderAzPrefix()
    {
        var cmd = new AzCommand("account list", new[] { "--all" });
        cmd.ToString().Should().StartWith("az account list");
    }
}
