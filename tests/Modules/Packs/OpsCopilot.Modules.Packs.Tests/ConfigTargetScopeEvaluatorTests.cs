using Microsoft.Extensions.Configuration;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.Packs.Infrastructure;
using Xunit;

namespace OpsCopilot.Modules.Packs.Tests;

/// <summary>
/// Unit tests for <see cref="ConfigTargetScopeEvaluator"/>.
/// Verifies the "empty = DENY" strict semantics, allowlist lookups,
/// case-insensitivity, and unknown target-type handling.
/// </summary>
public sealed class ConfigTargetScopeEvaluatorTests
{
    private const string TenantId = "tenant-scope-test";

    // ── Helpers ────────────────────────────────────────────────

    private static ConfigTargetScopeEvaluator CreateEvaluator(
        string[]? subscriptions = null,
        string[]? workspaces = null)
    {
        var data = new Dictionary<string, string?>();

        if (subscriptions is not null)
        {
            for (var i = 0; i < subscriptions.Length; i++)
                data[$"SafeActions:AllowedAzureSubscriptionIds:{i}"] = subscriptions[i];
        }

        if (workspaces is not null)
        {
            for (var i = 0; i < workspaces.Length; i++)
                data[$"SafeActions:AllowedLogAnalyticsWorkspaceIds:{i}"] = workspaces[i];
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();

        return new ConfigTargetScopeEvaluator(config);
    }

    // ── Subscription tests ────────────────────────────────────

    // 1. Empty subscription list → deny missing

    [Fact]
    public void Evaluate_EmptySubscriptions_DenyMissing()
    {
        var evaluator = CreateEvaluator();

        var decision = evaluator.Evaluate(TenantId, "azure_subscription", "sub-1");

        Assert.False(decision.Allowed);
        Assert.Equal("target_scope_missing_subscription", decision.ReasonCode);
    }

    // 2. Subscription in list → allow

    [Fact]
    public void Evaluate_SubscriptionInList_Allow()
    {
        var evaluator = CreateEvaluator(subscriptions: new[] { "sub-1", "sub-2" });

        var decision = evaluator.Evaluate(TenantId, "azure_subscription", "sub-1");

        Assert.True(decision.Allowed);
        Assert.Equal("ALLOWED", decision.ReasonCode);
    }

    // 3. Subscription not in list → deny not allowed

    [Fact]
    public void Evaluate_SubscriptionNotInList_DenyNotAllowed()
    {
        var evaluator = CreateEvaluator(subscriptions: new[] { "sub-1" });

        var decision = evaluator.Evaluate(TenantId, "azure_subscription", "sub-999");

        Assert.False(decision.Allowed);
        Assert.Equal("target_scope_subscription_not_allowed", decision.ReasonCode);
        Assert.Contains("sub-999", decision.Message);
    }

    // 4. Subscription match is case-insensitive

    [Fact]
    public void Evaluate_SubscriptionCaseInsensitive()
    {
        var evaluator = CreateEvaluator(subscriptions: new[] { "SUB-ABC" });

        var decision = evaluator.Evaluate(TenantId, "azure_subscription", "sub-abc");

        Assert.True(decision.Allowed);
    }

    // ── Workspace tests ───────────────────────────────────────

    // 5. Empty workspace list → deny missing

    [Fact]
    public void Evaluate_EmptyWorkspaces_DenyMissing()
    {
        var evaluator = CreateEvaluator();

        var decision = evaluator.Evaluate(TenantId, "log_analytics_workspace", "ws-1");

        Assert.False(decision.Allowed);
        Assert.Equal("target_scope_missing_workspace", decision.ReasonCode);
    }

    // 6. Workspace in list → allow

    [Fact]
    public void Evaluate_WorkspaceInList_Allow()
    {
        var evaluator = CreateEvaluator(workspaces: new[] { "ws-1", "ws-2" });

        var decision = evaluator.Evaluate(TenantId, "log_analytics_workspace", "ws-1");

        Assert.True(decision.Allowed);
        Assert.Equal("ALLOWED", decision.ReasonCode);
    }

    // 7. Workspace not in list → deny not allowed

    [Fact]
    public void Evaluate_WorkspaceNotInList_DenyNotAllowed()
    {
        var evaluator = CreateEvaluator(workspaces: new[] { "ws-1" });

        var decision = evaluator.Evaluate(TenantId, "log_analytics_workspace", "ws-999");

        Assert.False(decision.Allowed);
        Assert.Equal("target_scope_workspace_not_allowed", decision.ReasonCode);
        Assert.Contains("ws-999", decision.Message);
    }

    // 8. Workspace match is case-insensitive

    [Fact]
    public void Evaluate_WorkspaceCaseInsensitive()
    {
        var evaluator = CreateEvaluator(workspaces: new[] { "WS-XYZ" });

        var decision = evaluator.Evaluate(TenantId, "log_analytics_workspace", "ws-xyz");

        Assert.True(decision.Allowed);
    }

    // ── Unknown target type ───────────────────────────────────

    // 9. Unknown target type → deny

    [Fact]
    public void Evaluate_UnknownTargetType_Deny()
    {
        var evaluator = CreateEvaluator(
            subscriptions: new[] { "sub-1" },
            workspaces: new[] { "ws-1" });

        var decision = evaluator.Evaluate(TenantId, "cosmos_db_account", "acc-1");

        Assert.False(decision.Allowed);
        Assert.Equal("target_scope_unknown_target", decision.ReasonCode);
        Assert.Contains("cosmos_db_account", decision.Message);
    }

    // ── Null/missing config ───────────────────────────────────

    // 10. No config keys at all → both deny missing

    [Fact]
    public void Evaluate_NoConfigKeys_SubscriptionDenyMissing()
    {
        var config = new ConfigurationBuilder().Build();
        var evaluator = new ConfigTargetScopeEvaluator(config);

        var sub = evaluator.Evaluate(TenantId, "azure_subscription", "sub-1");
        var ws  = evaluator.Evaluate(TenantId, "log_analytics_workspace", "ws-1");

        Assert.False(sub.Allowed);
        Assert.Equal("target_scope_missing_subscription", sub.ReasonCode);
        Assert.False(ws.Allowed);
        Assert.Equal("target_scope_missing_workspace", ws.ReasonCode);
    }

    // 11. Multiple subscriptions, second matches

    [Fact]
    public void Evaluate_MultipleSubscriptions_SecondMatches()
    {
        var evaluator = CreateEvaluator(
            subscriptions: new[] { "sub-A", "sub-B", "sub-C" });

        var decision = evaluator.Evaluate(TenantId, "azure_subscription", "sub-B");

        Assert.True(decision.Allowed);
    }

    // 12. Multiple workspaces, last matches

    [Fact]
    public void Evaluate_MultipleWorkspaces_LastMatches()
    {
        var evaluator = CreateEvaluator(
            workspaces: new[] { "ws-A", "ws-B", "ws-C" });

        var decision = evaluator.Evaluate(TenantId, "log_analytics_workspace", "ws-C");

        Assert.True(decision.Allowed);
    }
}
