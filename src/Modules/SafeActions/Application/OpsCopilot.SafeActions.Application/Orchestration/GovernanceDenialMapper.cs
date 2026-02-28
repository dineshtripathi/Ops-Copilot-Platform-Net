using OpsCopilot.BuildingBlocks.Contracts.Governance;

namespace OpsCopilot.SafeActions.Application.Orchestration;

/// <summary>
/// Maps raw governance policy decisions to <see cref="PolicyDeniedException"/>
/// with frozen reason codes and structured messages.
/// Frozen codes: <c>governance_tool_denied</c>, <c>governance_budget_exceeded</c>.
/// </summary>
internal static class GovernanceDenialMapper
{
    /// <summary>Creates a denial for a governance tool-allowlist rejection.</summary>
    public static PolicyDeniedException ToolDenied(PolicyDecision decision)
        => new(
            "governance_tool_denied",
            $"Denied by governance tool allowlist (policyReason={decision.ReasonCode}): {decision.Message}");

    /// <summary>Creates a denial for a governance token-budget rejection or overage.</summary>
    public static PolicyDeniedException BudgetDenied(
        BudgetDecision decision,
        int requestedTokens)
        => new(
            "governance_budget_exceeded",
            $"Denied by governance token budget "
          + $"(policyReason={decision.ReasonCode}, "
          + $"requestedTokens={requestedTokens}, "
          + $"maxTokens={decision.MaxTokens?.ToString() ?? "null"}): "
          + decision.Message);
}
