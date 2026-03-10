using Moq;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Infrastructure.Routing;
using OpsCopilot.Tenancy.Application.Abstractions;
using OpsCopilot.Tenancy.Domain.Entities;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

public sealed class TenantConfigModelRoutingPolicyTests
{
    private static readonly Guid   TenantGuid = Guid.NewGuid();
    private static readonly string TenantId   = TenantGuid.ToString();

    [Fact]
    public async Task SelectModelAsync_WithMatchingConfig_ReturnsConfiguredModel()
    {
        var store = new Mock<ITenantConfigStore>(MockBehavior.Strict);
        store.Setup(s => s.GetAsync(TenantGuid, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<TenantConfigEntry>
             {
                 TenantConfigEntry.Create(TenantGuid, "model:triage", "gpt-4o")
             });
        var sut = new TenantConfigModelRoutingPolicy(store.Object);

        var result = await sut.SelectModelAsync(TenantId);

        Assert.Equal("gpt-4o", result.ModelId);
    }

    [Fact]
    public async Task SelectModelAsync_WithNoMatchingKey_ReturnsDefault()
    {
        var store = new Mock<ITenantConfigStore>(MockBehavior.Strict);
        store.Setup(s => s.GetAsync(TenantGuid, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<TenantConfigEntry>
             {
                 TenantConfigEntry.Create(TenantGuid, "something:else", "irrelevant")
             });
        var sut = new TenantConfigModelRoutingPolicy(store.Object);

        var result = await sut.SelectModelAsync(TenantId);

        Assert.Equal("default", result.ModelId);
    }

    [Fact]
    public async Task SelectModelAsync_WithInvalidTenantId_ReturnsDefault_StoreNotCalled()
    {
        var store = new Mock<ITenantConfigStore>(MockBehavior.Strict);
        var sut   = new TenantConfigModelRoutingPolicy(store.Object);

        var result = await sut.SelectModelAsync("not-a-guid");

        Assert.Equal("default", result.ModelId);
        store.VerifyNoOtherCalls();
    }
}
