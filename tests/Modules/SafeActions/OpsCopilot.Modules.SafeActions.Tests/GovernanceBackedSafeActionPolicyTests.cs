using Moq;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Infrastructure.Policies;
using Xunit;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// Unit tests for <see cref="GovernanceBackedSafeActionPolicy"/> proving
/// delegation to <see cref="IGovernancePolicyClient.EvaluateToolAllowlist"/>,
/// frozen reason codes, and policyReason traceability.
/// </summary>
public class GovernanceBackedSafeActionPolicyTests
{
    // ── Helpers ──────────────────────────────────────────────────

    private static (GovernanceBackedSafeActionPolicy Policy, Mock<IGovernancePolicyClient> Client)
        CreatePolicy(PolicyDecision decision)
    {
        var client = new Mock<IGovernancePolicyClient>(MockBehavior.Strict);
        client.Setup(c => c.EvaluateToolAllowlist(It.IsAny<string>(), It.IsAny<string>()))
              .Returns(decision);

        var policy = new GovernanceBackedSafeActionPolicy(client.Object);
        return (policy, client);
    }

    // ── 1. Allow passthrough ────────────────────────────────────

    [Fact]
    public void Evaluate_ReturnsAllow_WhenGovernanceAllows()
    {
        var (policy, _) = CreatePolicy(PolicyDecision.Allow());

        var result = policy.Evaluate("t-1", "restart_pod");

        Assert.True(result.Allowed);
    }

    // ── 2. Deny returns frozen code ─────────────────────────────

    [Fact]
    public void Evaluate_ReturnsDeny_WithFrozenCode_WhenGovernanceDenies()
    {
        var deny = PolicyDecision.Deny("tool_not_in_allowlist", "restart_pod not permitted");
        var (policy, _) = CreatePolicy(deny);

        var result = policy.Evaluate("t-blocked", "restart_pod");

        Assert.False(result.Allowed);
        Assert.Equal("governance_tool_denied", result.ReasonCode);
    }

    // ── 3. policyReason in message ──────────────────────────────

    [Fact]
    public void Evaluate_DenyMessage_ContainsPolicyReason()
    {
        var deny = PolicyDecision.Deny("tool_not_in_allowlist", "restart_pod not permitted");
        var (policy, _) = CreatePolicy(deny);

        var result = policy.Evaluate("t-blocked", "restart_pod");

        Assert.Contains("policyReason=tool_not_in_allowlist", result.Message);
    }

    // ── 4. Raw reason code preserved in policyReason ────────────

    [Theory]
    [InlineData("tool_not_in_allowlist")]
    [InlineData("action_disabled_by_admin")]
    [InlineData("custom_rule_xyz")]
    public void Evaluate_DenyMessage_PreservesRawReasonCode(string rawCode)
    {
        var deny = PolicyDecision.Deny(rawCode, "some message");
        var (policy, _) = CreatePolicy(deny);

        var result = policy.Evaluate("t-any", "any_action");

        Assert.Contains($"policyReason={rawCode}", result.Message);
    }

    // ── 5. Tenant isolation — different tenants get different results ─

    [Fact]
    public void Evaluate_DifferentTenants_ReceiveDifferentDecisions()
    {
        var client = new Mock<IGovernancePolicyClient>(MockBehavior.Strict);
        client.Setup(c => c.EvaluateToolAllowlist("t-allowed", "restart_pod"))
              .Returns(PolicyDecision.Allow());
        client.Setup(c => c.EvaluateToolAllowlist("t-blocked", "restart_pod"))
              .Returns(PolicyDecision.Deny("tool_not_in_allowlist", "blocked"));

        var policy = new GovernanceBackedSafeActionPolicy(client.Object);

        var allowResult = policy.Evaluate("t-allowed", "restart_pod");
        var denyResult = policy.Evaluate("t-blocked", "restart_pod");

        Assert.True(allowResult.Allowed);
        Assert.False(denyResult.Allowed);
    }

    // ── 6. Exactly-once delegation ──────────────────────────────

    [Fact]
    public void Evaluate_InvokesGovernanceClient_ExactlyOnce()
    {
        var client = new Mock<IGovernancePolicyClient>(MockBehavior.Strict);
        client.Setup(c => c.EvaluateToolAllowlist("t-1", "restart_pod"))
              .Returns(PolicyDecision.Allow());

        var policy = new GovernanceBackedSafeActionPolicy(client.Object);
        policy.Evaluate("t-1", "restart_pod");

        client.Verify(
            c => c.EvaluateToolAllowlist("t-1", "restart_pod"),
            Times.Once);
    }

    // ── 7. Deny message contains raw message text ───────────────

    [Fact]
    public void Evaluate_DenyMessage_ContainsRawGovernanceMessage()
    {
        var deny = PolicyDecision.Deny("tool_not_in_allowlist", "restart_pod not in tenant allowlist");
        var (policy, _) = CreatePolicy(deny);

        var result = policy.Evaluate("t-blocked", "restart_pod");

        Assert.Contains("restart_pod not in tenant allowlist", result.Message);
    }

    // ── 8. Null client throws ───────────────────────────────────

    [Fact]
    public void Constructor_ThrowsArgumentNull_WhenClientIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new GovernanceBackedSafeActionPolicy(null!));
    }

    // ── 9. Allow decision has empty ReasonCode and Message ──────

    [Fact]
    public void Evaluate_AllowDecision_HasDefaultReasonCodeAndMessage()
    {
        var (policy, _) = CreatePolicy(PolicyDecision.Allow());

        var result = policy.Evaluate("t-1", "restart_pod");

        Assert.True(result.Allowed);
        Assert.Equal("ALLOWED", result.ReasonCode);
        Assert.Equal("Policy check passed.", result.Message);
    }
}
