using Microsoft.Extensions.Configuration;
using OpsCopilot.AgentRuns.Infrastructure.Routing;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

public sealed class ConfigPromptVersionServiceTests
{
    [Fact]
    public async Task GetCurrentVersionAsync_WithConfigValues_ReturnsConfiguredVersion()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Prompting:triage:VersionId"]   = "1.2.3",
                ["Prompting:triage:SystemPrompt"] = "Custom system prompt."
            })
            .Build();
        var sut = new ConfigPromptVersionService(config);

        var result = await sut.GetCurrentVersionAsync("triage");

        Assert.Equal("1.2.3",                result.VersionId);
        Assert.Equal("Custom system prompt.", result.SystemPrompt);
    }

    [Fact]
    public async Task GetCurrentVersionAsync_WithMissingConfig_ReturnsFallbacks()
    {
        var config = new ConfigurationBuilder().Build();
        var sut    = new ConfigPromptVersionService(config);

        var result = await sut.GetCurrentVersionAsync("triage");

        Assert.Equal("0.0.0",                              result.VersionId);
        Assert.Equal("Analyze the following triage data.", result.SystemPrompt);
    }
}
