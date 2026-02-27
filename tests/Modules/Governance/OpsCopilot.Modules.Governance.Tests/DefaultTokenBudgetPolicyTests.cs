using Moq;
using OpsCopilot.Governance.Application.Configuration;
using OpsCopilot.Governance.Application.Policies;
using OpsCopilot.Governance.Application.Services;
using Xunit;

namespace OpsCopilot.Modules.Governance.Tests;

public sealed class DefaultTokenBudgetPolicyTests
{
    private const string TenantId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    // ── 1. No budget (null) → unlimited ──────────────────────────────

    [Fact]
    public void CheckRunBudget_NullBudget_ReturnsAllowUnlimited()
    {
        var resolver = new Mock<ITenantAwareGovernanceOptionsResolver>();
        resolver.Setup(r => r.Resolve(TenantId))
            .Returns(new ResolvedGovernanceOptions([], TokenBudget: null, SessionTtlMinutes: 30));

        var sut = new DefaultTokenBudgetPolicy(resolver.Object);

        var result = sut.CheckRunBudget(TenantId, Guid.NewGuid());

        Assert.True(result.Allowed);
        Assert.Null(result.MaxTokens);
    }

    // ── 2. With budget → Allow capped ────────────────────────────────

    [Fact]
    public void CheckRunBudget_WithBudget_ReturnsAllowWithMax()
    {
        var resolver = new Mock<ITenantAwareGovernanceOptionsResolver>();
        resolver.Setup(r => r.Resolve(TenantId))
            .Returns(new ResolvedGovernanceOptions([], TokenBudget: 4096, SessionTtlMinutes: 30));

        var sut = new DefaultTokenBudgetPolicy(resolver.Object);

        var result = sut.CheckRunBudget(TenantId, Guid.NewGuid());

        Assert.True(result.Allowed);
        Assert.Equal(4096, result.MaxTokens);
    }
}
