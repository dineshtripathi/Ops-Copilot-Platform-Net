using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.SafeActions.Application.Abstractions;

namespace OpsCopilot.SafeActions.Infrastructure.Policies;

/// <summary>
/// Tenant-aware safe-action policy that delegates to the governance tool
/// allowlist via <see cref="IGovernancePolicyClient"/>.
/// Replaces the always-allow <see cref="DefaultSafeActionPolicy"/> stub.
/// </summary>
/// <remarks>
/// Frozen reason code: <c>governance_tool_denied</c>.
/// Message always contains <c>policyReason=&lt;raw&gt;</c> for traceability.
/// </remarks>
internal sealed class GovernanceBackedSafeActionPolicy : ISafeActionPolicy
{
    private readonly IGovernancePolicyClient _governanceClient;

    public GovernanceBackedSafeActionPolicy(IGovernancePolicyClient governanceClient)
    {
        _governanceClient = governanceClient ?? throw new ArgumentNullException(nameof(governanceClient));
    }

    public PolicyDecision Evaluate(string tenantId, string actionType)
    {
        var decision = _governanceClient.EvaluateToolAllowlist(tenantId, actionType);

        if (decision.Allowed)
            return PolicyDecision.Allow();

        return PolicyDecision.Deny(
            "governance_tool_denied",
            $"Denied by governance tool allowlist (policyReason={decision.ReasonCode}): {decision.Message}");
    }
}
