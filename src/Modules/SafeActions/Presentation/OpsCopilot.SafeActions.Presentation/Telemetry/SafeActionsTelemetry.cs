using System.Diagnostics.Metrics;
using OpsCopilot.SafeActions.Application.Abstractions;

namespace OpsCopilot.SafeActions.Presentation.Telemetry;

/// <summary>
/// Production implementation of <see cref="ISafeActionsTelemetry"/>
/// backed by <c>System.Diagnostics.Metrics</c>.
/// Meter name: <c>OpsCopilot.SafeActions</c>.
/// Registered as a singleton — counters are thread-safe.
/// </summary>
public sealed class SafeActionsTelemetry : ISafeActionsTelemetry, IDisposable
{
    internal const string MeterName = "OpsCopilot.SafeActions";

    private readonly Meter _meter;

    private readonly Counter<long> _executionAttempts;
    private readonly Counter<long> _executionSuccess;
    private readonly Counter<long> _executionFailure;
    private readonly Counter<long> _guarded501;
    private readonly Counter<long> _replayConflict;
    private readonly Counter<long> _policyDenied;
    private readonly Counter<long> _identityMissing401;
    private readonly Counter<long> _approvalDecisions;
    private readonly Counter<long> _queryRequests;
    private readonly Counter<long> _queryValidationFailures;

    public SafeActionsTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _executionAttempts       = _meter.CreateCounter<long>(
            "safeactions.execution.attempts",
            description: "Number of execution / rollback-execution attempts");

        _executionSuccess        = _meter.CreateCounter<long>(
            "safeactions.execution.success",
            description: "Number of successful executions");

        _executionFailure        = _meter.CreateCounter<long>(
            "safeactions.execution.failure",
            description: "Number of failed executions");

        _guarded501              = _meter.CreateCounter<long>(
            "safeactions.execution.guarded_501",
            description: "Execution blocked by 501 feature guard");

        _replayConflict          = _meter.CreateCounter<long>(
            "safeactions.execution.replay_conflict",
            description: "Replay conflict blocked by status guard");

        _policyDenied            = _meter.CreateCounter<long>(
            "safeactions.execution.policy_denied",
            description: "Execution denied by policy gate");

        _identityMissing401      = _meter.CreateCounter<long>(
            "safeactions.identity.missing_401",
            description: "Requests rejected due to missing identity");

        _approvalDecisions       = _meter.CreateCounter<long>(
            "safeactions.approval.decisions",
            description: "Approval / rejection decisions");

        _queryRequests           = _meter.CreateCounter<long>(
            "safeactions.query.requests",
            description: "Query requests (list / detail)");

        _queryValidationFailures = _meter.CreateCounter<long>(
            "safeactions.query.validation_failures",
            description: "Query validation failures");
    }

    // ── Counter recording methods ────────────────────────────────

    public void RecordExecutionAttempt(string actionType, string tenantId)
        => _executionAttempts.Add(1,
            new KeyValuePair<string, object?>("action_type", actionType),
            new KeyValuePair<string, object?>("tenant_id", tenantId));

    public void RecordExecutionSuccess(string actionType, string tenantId)
        => _executionSuccess.Add(1,
            new KeyValuePair<string, object?>("action_type", actionType),
            new KeyValuePair<string, object?>("tenant_id", tenantId));

    public void RecordExecutionFailure(string actionType, string tenantId)
        => _executionFailure.Add(1,
            new KeyValuePair<string, object?>("action_type", actionType),
            new KeyValuePair<string, object?>("tenant_id", tenantId));

    public void RecordGuarded501(string endpoint)
        => _guarded501.Add(1,
            new KeyValuePair<string, object?>("endpoint", endpoint));

    public void RecordReplayConflict(string actionType)
        => _replayConflict.Add(1,
            new KeyValuePair<string, object?>("action_type", actionType));

    public void RecordPolicyDenied(string actionType, string tenantId)
        => _policyDenied.Add(1,
            new KeyValuePair<string, object?>("action_type", actionType),
            new KeyValuePair<string, object?>("tenant_id", tenantId));

    public void RecordIdentityMissing401(string endpoint)
        => _identityMissing401.Add(1,
            new KeyValuePair<string, object?>("endpoint", endpoint));

    public void RecordApprovalDecision(string decision)
        => _approvalDecisions.Add(1,
            new KeyValuePair<string, object?>("decision", decision));

    public void RecordQueryRequest(string queryKind)
        => _queryRequests.Add(1,
            new KeyValuePair<string, object?>("query_kind", queryKind));

    public void RecordQueryValidationFailure()
        => _queryValidationFailures.Add(1);

    public void Dispose() => _meter.Dispose();
}
