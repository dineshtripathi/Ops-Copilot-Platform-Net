using OpsCopilot.BuildingBlocks.Contracts.Governance;

namespace OpsCopilot.SafeActions.Application.Abstractions;

/// <summary>
/// Bridge contract that decouples SafeActions from Governance implementation details.
/// SafeActions consumes governance decisions without knowing how policies are resolved.
/// </summary>
public interface IGovernancePolicyClient
{
    /// <summary>
    /// Checks whether the given action type is permitted by the tenant's tool allowlist.
    /// </summary>
    PolicyDecision EvaluateToolAllowlist(string tenantId, string actionType);

    /// <summary>
    /// Checks whether the tenant's token budget permits the estimated token cost.
    /// </summary>
    BudgetDecision EvaluateTokenBudget(string tenantId, string actionType, int? requestedTokens = null);
}
