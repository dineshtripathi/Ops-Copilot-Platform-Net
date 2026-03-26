using Moq;
using OpsCopilot.Prompting.Application.Services;
using OpsCopilot.Prompting.Domain.Entities;
using OpsCopilot.Prompting.Domain.Repositories;
using Xunit;

namespace OpsCopilot.Modules.Prompting.Tests;

public sealed class PromptRegistryServiceTests
{
    [Fact]
    public async Task ResolveAsync_DelegatesToRepository_ReturnsTemplate()
    {
        var expected = PromptTemplate.Create("triage", "content");
        var repo = new Mock<IPromptTemplateRepository>();
        repo.Setup(r => r.FindActiveAsync("triage", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var svc = new PromptRegistryService(repo.Object);

        var result = await svc.ResolveAsync("triage");

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task ResolveAsync_NoActiveTemplate_ReturnsNull()
    {
        var repo = new Mock<IPromptTemplateRepository>();
        repo.Setup(r => r.FindActiveAsync("chat", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PromptTemplate?)null);
        var svc = new PromptRegistryService(repo.Object);

        var result = await svc.ResolveAsync("chat");

        Assert.Null(result);
    }
}
