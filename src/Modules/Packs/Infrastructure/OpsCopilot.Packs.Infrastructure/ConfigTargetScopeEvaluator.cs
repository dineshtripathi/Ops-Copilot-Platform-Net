using Microsoft.Extensions.Configuration;
using OpsCopilot.BuildingBlocks.Contracts.Governance;

namespace OpsCopilot.Packs.Infrastructure;

/// <summary>
/// Evaluates target scope allowlists from configuration.
/// Empty list = DENY (strict default — opposite of executor "empty = allow all").
/// </summary>
internal sealed class ConfigTargetScopeEvaluator : ITargetScopeEvaluator
{
    private readonly HashSet<string> _allowedSubscriptions;
    private readonly HashSet<string> _allowedWorkspaces;

    public ConfigTargetScopeEvaluator(IConfiguration configuration)
    {
        _allowedSubscriptions = ToSet(
            configuration.GetSection("SafeActions:AllowedAzureSubscriptionIds").Get<string[]>());
        _allowedWorkspaces = ToSet(
            configuration.GetSection("SafeActions:AllowedLogAnalyticsWorkspaceIds").Get<string[]>());
    }

    public TargetScopeDecision Evaluate(string tenantId, string targetType, string targetValue)
    {
        return targetType switch
        {
            "azure_subscription" => EvaluateSubscription(targetValue),
            "log_analytics_workspace" => EvaluateWorkspace(targetValue),
            _ => TargetScopeDecision.Deny("target_scope_unknown_target",
                $"Unknown target type '{targetType}'.")
        };
    }

    private TargetScopeDecision EvaluateSubscription(string value)
    {
        if (_allowedSubscriptions.Count == 0)
            return TargetScopeDecision.Deny("target_scope_missing_subscription",
                "No Azure subscriptions are configured in the allowlist.");

        if (!_allowedSubscriptions.Contains(value))
            return TargetScopeDecision.Deny("target_scope_subscription_not_allowed",
                $"Azure subscription '{value}' is not in the allowlist.");

        return TargetScopeDecision.Allow();
    }

    private TargetScopeDecision EvaluateWorkspace(string value)
    {
        if (_allowedWorkspaces.Count == 0)
            return TargetScopeDecision.Deny("target_scope_missing_workspace",
                "No Log Analytics workspaces are configured in the allowlist.");

        if (!_allowedWorkspaces.Contains(value))
            return TargetScopeDecision.Deny("target_scope_workspace_not_allowed",
                $"Log Analytics workspace '{value}' is not in the allowlist.");

        return TargetScopeDecision.Allow();
    }

    private static HashSet<string> ToSet(string[]? values) =>
        values is { Length: > 0 }
            ? new HashSet<string>(values, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
