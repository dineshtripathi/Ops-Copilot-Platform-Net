using OpsCopilot.BuildingBlocks.Contracts.Governance;

namespace OpsCopilot.SafeActions.Application.Abstractions;

/// <summary>
/// Evaluates whether a proposed safe action is permitted by governance policy.
/// Called before the <see cref="Domain.Entities.ActionRecord"/> row is persisted,
/// ensuring policy denials never create a database record.
/// </summary>
public interface ISafeActionPolicy
{
    /// <summary>
    /// Evaluates whether the given action type is allowed for the tenant.
    /// </summary>
    PolicyDecision Evaluate(string tenantId, string actionType);
}
