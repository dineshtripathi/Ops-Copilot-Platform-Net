using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.SafeActions.Application.Abstractions;

namespace OpsCopilot.SafeActions.Infrastructure.Policies;

/// <summary>
/// Default (always-allow) safe-action policy used until a real governance
/// implementation is wired. Satisfies the <see cref="ISafeActionPolicy"/>
/// contract without blocking any action types.
/// </summary>
public sealed class DefaultSafeActionPolicy : ISafeActionPolicy
{
    public PolicyDecision Evaluate(string tenantId, string actionType)
        => PolicyDecision.Allow();
}
