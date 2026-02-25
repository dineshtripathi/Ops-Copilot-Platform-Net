using OpsCopilot.BuildingBlocks.Contracts.Governance;

namespace OpsCopilot.SafeActions.Application.Abstractions;

/// <summary>
/// Evaluates whether a tenant is authorized to <em>execute</em> a given action type.
/// Enforced at execution time (both forward and rollback), <strong>after</strong>
/// the proposal-time <see cref="ISafeActionPolicy"/> has already allowed the action
/// to be recorded.
/// <para>
/// <strong>Strict / secure-by-default:</strong> missing configuration or an empty
/// tenant list for a given action type results in <see cref="PolicyDecision.Deny"/>.
/// </para>
/// </summary>
public interface ITenantExecutionPolicy
{
    /// <summary>
    /// Determines whether <paramref name="tenantId"/> may execute <paramref name="actionType"/>.
    /// </summary>
    PolicyDecision EvaluateExecution(string tenantId, string actionType);
}
