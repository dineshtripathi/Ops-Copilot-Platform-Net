using Moq;
using OpsCopilot.Governance.Application.Configuration;
using OpsCopilot.Governance.Application.Policies;
using OpsCopilot.Governance.Application.Services;
using Xunit;

namespace OpsCopilot.Modules.Governance.Tests;

public sealed class DefaultToolAllowlistPolicyTests
{
    private const string TenantId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    private static Mock<ITenantAwareGovernanceOptionsResolver> MockResolver(
        List<string> allowedTools)
    {
        var mock = new Mock<ITenantAwareGovernanceOptionsResolver>();
        mock.Setup(r => r.Resolve(TenantId))
            .Returns(new ResolvedGovernanceOptions(allowedTools, TokenBudget: null, SessionTtlMinutes: 30));
        return mock;
    }

    // ── 1. Tool in allowlist → Allow ─────────────────────────────────

    [Fact]
    public void CanUseTool_ToolInAllowlist_ReturnsAllow()
    {
        var resolver = MockResolver(["kql_query", "runbook_search"]);
        var sut = new DefaultToolAllowlistPolicy(resolver.Object);

        var result = sut.CanUseTool(TenantId, "kql_query");

        Assert.True(result.Allowed);
    }

    // ── 2. Tool NOT in allowlist → Deny ──────────────────────────────

    [Fact]
    public void CanUseTool_ToolNotInAllowlist_ReturnsDeny()
    {
        var resolver = MockResolver(["kql_query"]);
        var sut = new DefaultToolAllowlistPolicy(resolver.Object);

        var result = sut.CanUseTool(TenantId, "dangerous_tool");

        Assert.False(result.Allowed);
        Assert.Equal("TOOL_DENIED", result.ReasonCode);
    }

    // ── 3. Empty allowlist → Allow all tools ─────────────────────────

    [Fact]
    public void CanUseTool_EmptyAllowlist_AllowsAll()
    {
        var resolver = MockResolver([]);
        var sut = new DefaultToolAllowlistPolicy(resolver.Object);

        var result = sut.CanUseTool(TenantId, "any_tool");

        Assert.True(result.Allowed);
    }

    // ── 4. Case-insensitive match ────────────────────────────────────

    [Fact]
    public void CanUseTool_CaseInsensitiveMatch_ReturnsAllow()
    {
        var resolver = MockResolver(["KQL_Query"]);
        var sut = new DefaultToolAllowlistPolicy(resolver.Object);

        var result = sut.CanUseTool(TenantId, "kql_query");

        Assert.True(result.Allowed);
    }
}
