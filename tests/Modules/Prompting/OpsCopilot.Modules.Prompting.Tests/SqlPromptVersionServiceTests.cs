using Moq;
using OpsCopilot.Prompting.Application.Abstractions;
using OpsCopilot.Prompting.Domain.Entities;
using OpsCopilot.Prompting.Infrastructure.Adapters;
using Xunit;

namespace OpsCopilot.Modules.Prompting.Tests;

public sealed class SqlPromptVersionServiceTests
{
    [Fact]
    public async Task GetCurrentVersionAsync_WithTemplate_ReturnsVersionedPrompt()
    {
        var template = PromptTemplate.Create("triage", "Custom SRE prompt", version: 3);
        var registry = new Mock<IPromptRegistry>();
        registry.Setup(r => r.ResolveAsync("triage", It.IsAny<CancellationToken>()))
                .ReturnsAsync(template);
        var svc = new SqlPromptVersionService(registry.Object);

        var result = await svc.GetCurrentVersionAsync("triage");

        Assert.Equal("v3", result.VersionId);
        Assert.Equal("Custom SRE prompt", result.SystemPrompt);
    }

    [Fact]
    public async Task GetCurrentVersionAsync_NoTemplate_TriageKeyReturnsDefaultWithSRE()
    {
        var registry = new Mock<IPromptRegistry>();
        registry.Setup(r => r.ResolveAsync("triage", It.IsAny<CancellationToken>()))
                .ReturnsAsync((PromptTemplate?)null);
        var svc = new SqlPromptVersionService(registry.Object);

        var result = await svc.GetCurrentVersionAsync("triage");

        Assert.Equal("v1-default", result.VersionId);
        Assert.Contains("SRE", result.SystemPrompt);
    }

    [Fact]
    public async Task GetCurrentVersionAsync_NoTemplate_ChatKeyReturnsDefaultWithIncident()
    {
        var registry = new Mock<IPromptRegistry>();
        registry.Setup(r => r.ResolveAsync("chat", It.IsAny<CancellationToken>()))
                .ReturnsAsync((PromptTemplate?)null);
        var svc = new SqlPromptVersionService(registry.Object);

        var result = await svc.GetCurrentVersionAsync("chat");

        Assert.Equal("v1-default", result.VersionId);
        Assert.Contains("incident", result.SystemPrompt);
    }

    [Fact]
    public async Task GetCurrentVersionAsync_UnknownKey_ReturnsFallbackDefault()
    {
        var registry = new Mock<IPromptRegistry>();
        registry.Setup(r => r.ResolveAsync("unknown", It.IsAny<CancellationToken>()))
                .ReturnsAsync((PromptTemplate?)null);
        var svc = new SqlPromptVersionService(registry.Object);

        var result = await svc.GetCurrentVersionAsync("unknown");

        Assert.Equal("v1-default", result.VersionId);
        Assert.NotEmpty(result.SystemPrompt);
    }
}
