using Moq;
using OpsCopilot.Prompting.Application.Abstractions;
using OpsCopilot.Prompting.Application.Models;
using OpsCopilot.Prompting.Application.Services;
using OpsCopilot.Prompting.Domain.Entities;
using Xunit;

namespace OpsCopilot.Modules.Prompting.Tests;

public sealed class CanaryPromptStrategyTests
{
    private static CanaryPromptStrategy CreateStrategy(
        IPromptRegistry inner, ICanaryStore store)
        => new(inner, store);

    [Fact]
    public async Task ResolveAsync_NoCanary_DelegatesToInner()
    {
        var expected = PromptTemplate.Create("triage", "original");
        var inner = new Mock<IPromptRegistry>();
        inner.Setup(r => r.ResolveAsync("triage", It.IsAny<CancellationToken>()))
             .ReturnsAsync(expected);
        var store = new InMemoryCanaryStore();
        var strategy = CreateStrategy(inner.Object, store);

        var result = await strategy.ResolveAsync("triage");

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task ResolveAsync_FullTraffic_AlwaysReturnsCandidate()
    {
        var inner = new Mock<IPromptRegistry>();
        inner.Setup(r => r.ResolveAsync("triage", It.IsAny<CancellationToken>()))
             .ReturnsAsync(PromptTemplate.Create("triage", "original"));
        var store = new InMemoryCanaryStore();
        store.SetCanary("triage", new CanaryState("triage", 2, "candidate content", 100, DateTimeOffset.UtcNow));
        var strategy = CreateStrategy(inner.Object, store);

        // With 100% traffic, every call should return the candidate.
        for (var i = 0; i < 20; i++)
        {
            var result = await strategy.ResolveAsync("triage");
            Assert.NotNull(result);
            Assert.Equal("candidate content", result!.Content);
            Assert.Equal(2, result.Version);
        }
    }

    [Fact]
    public async Task ResolveAsync_ZeroTraffic_NeverReturnsCandidate()
    {
        var original = PromptTemplate.Create("triage", "original");
        var inner = new Mock<IPromptRegistry>();
        inner.Setup(r => r.ResolveAsync("triage", It.IsAny<CancellationToken>()))
             .ReturnsAsync(original);
        var store = new InMemoryCanaryStore();
        store.SetCanary("triage", new CanaryState("triage", 2, "candidate", 0, DateTimeOffset.UtcNow));
        var strategy = CreateStrategy(inner.Object, store);

        // With 0% traffic, every call should return the original.
        for (var i = 0; i < 20; i++)
        {
            var result = await strategy.ResolveAsync("triage");
            Assert.Same(original, result);
        }
    }
}

public sealed class PromotionGateServiceTests
{
    [Fact]
    public void Evaluate_AboveThreshold_ReturnsPromote()
    {
        var store = new InMemoryCanaryStore();
        store.SetCanary("triage", new CanaryState("triage", 2, "candidate", 50, DateTimeOffset.UtcNow));
        var gate = new PromotionGateService(store);

        var result = gate.Evaluate("triage", 0.85f);

        Assert.Equal(PromotionResult.Promote, result);
    }

    [Fact]
    public void Evaluate_BelowThreshold_ReturnsReject()
    {
        var store = new InMemoryCanaryStore();
        store.SetCanary("triage", new CanaryState("triage", 2, "candidate", 50, DateTimeOffset.UtcNow));
        var gate = new PromotionGateService(store);

        var result = gate.Evaluate("triage", 0.55f);

        Assert.Equal(PromotionResult.Reject, result);
    }

    [Fact]
    public void Evaluate_NoCanary_ReturnsNoCanary()
    {
        var store = new InMemoryCanaryStore();
        var gate = new PromotionGateService(store);

        var result = gate.Evaluate("triage", 0.99f);

        Assert.Equal(PromotionResult.NoCanary, result);
    }
}

public sealed class InMemoryCanaryStoreTests
{
    [Fact]
    public void SetAndGet_RoundTrips()
    {
        var store = new InMemoryCanaryStore();
        var state = new CanaryState("triage", 2, "content", 50, DateTimeOffset.UtcNow);

        store.SetCanary("triage", state);

        Assert.Same(state, store.GetCanary("triage"));
    }

    [Fact]
    public void Remove_ClearsCanary()
    {
        var store = new InMemoryCanaryStore();
        store.SetCanary("triage", new CanaryState("triage", 2, "content", 50, DateTimeOffset.UtcNow));

        store.RemoveCanary("triage");

        Assert.Null(store.GetCanary("triage"));
    }
}
