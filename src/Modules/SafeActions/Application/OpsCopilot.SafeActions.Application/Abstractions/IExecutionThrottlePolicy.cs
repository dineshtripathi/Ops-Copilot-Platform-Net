namespace OpsCopilot.SafeActions.Application.Abstractions;

/// <summary>
/// Abstraction for in-process execution throttle policy.
/// Implementations decide whether an execution attempt should be allowed
/// based on tenant, action type, and operation kind.
/// </summary>
public interface IExecutionThrottlePolicy
{
    /// <summary>
    /// Evaluates whether the execution attempt should be throttled.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="actionType">The action type (e.g. restart_pod).</param>
    /// <param name="operationKind">The operation kind (execute or rollback_execute).</param>
    /// <returns>A <see cref="ThrottleDecision"/> indicating whether the attempt is allowed.</returns>
    ThrottleDecision Evaluate(string tenantId, string actionType, string operationKind);
}
