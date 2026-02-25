namespace OpsCopilot.SafeActions.Application.Abstractions;

/// <summary>
/// Abstraction for SafeActions module observability counters.
/// Backed by a <c>System.Diagnostics.Metrics.Meter</c> named
/// <c>OpsCopilot.SafeActions</c>.
/// Registered as a singleton — all counter state is thread-safe.
/// </summary>
public interface ISafeActionsTelemetry
{
    /// <summary>safeactions.execution.attempts — incremented at the start of every execute / rollback-execute call.</summary>
    void RecordExecutionAttempt(string actionType, string tenantId);

    /// <summary>safeactions.execution.success — incremented when execution completes successfully.</summary>
    void RecordExecutionSuccess(string actionType, string tenantId);

    /// <summary>safeactions.execution.failure — incremented when execution fails.</summary>
    void RecordExecutionFailure(string actionType, string tenantId);

    /// <summary>safeactions.execution.guarded_501 — incremented when the 501 feature guard blocks execution.</summary>
    void RecordGuarded501(string endpoint);

    /// <summary>safeactions.execution.replay_conflict — incremented when a replay guard rejects a duplicate execution attempt.</summary>
    void RecordReplayConflict(string actionType);

    /// <summary>safeactions.execution.policy_denied — incremented when a policy gate denies the operation.</summary>
    void RecordPolicyDenied(string actionType, string tenantId);

    /// <summary>safeactions.identity.missing_401 — incremented when identity resolution returns null.</summary>
    void RecordIdentityMissing401(string endpoint);

    /// <summary>safeactions.approval.decisions — incremented per approval decision (tag: decision=approve|reject|rollback_approve).</summary>
    void RecordApprovalDecision(string decision);

    /// <summary>safeactions.query.requests — incremented per query (tag: query_kind=list|detail).</summary>
    void RecordQueryRequest(string queryKind);

    /// <summary>safeactions.query.validation_failures — incremented when a query request fails validation.</summary>
    void RecordQueryValidationFailure();
}
