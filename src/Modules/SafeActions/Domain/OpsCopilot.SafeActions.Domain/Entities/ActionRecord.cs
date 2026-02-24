using OpsCopilot.SafeActions.Domain.Enums;

namespace OpsCopilot.SafeActions.Domain.Entities;

/// <summary>
/// Aggregate root for a safe action lifecycle.
/// Tracks proposed action, approval gating, execution, outcome, and rollback.
/// Hard invariants:
///   - Actions never execute silently (approval required before execution).
///   - Rollback requires separate approval and is fully auditable.
/// </summary>
public sealed class ActionRecord
{
    // EF Core constructor
    private ActionRecord() { }

    public static ActionRecord Create(
        string  tenantId,
        Guid    runId,
        string  actionType,
        string  proposedPayloadJson,
        string? rollbackPayloadJson    = null,
        string? manualRollbackGuidance = null)
        => new()
        {
            ActionRecordId         = Guid.NewGuid(),
            TenantId               = tenantId,
            RunId                  = runId,
            ActionType             = actionType,
            ProposedPayloadJson    = proposedPayloadJson,
            Status                 = ActionStatus.Proposed,
            RollbackStatus         = rollbackPayloadJson is not null
                                         ? Enums.RollbackStatus.Available
                                         : manualRollbackGuidance is not null
                                             ? Enums.RollbackStatus.ManualRequired
                                             : Enums.RollbackStatus.None,
            RollbackPayloadJson    = rollbackPayloadJson,
            ManualRollbackGuidance = manualRollbackGuidance,
            CreatedAtUtc           = DateTimeOffset.UtcNow,
        };

    public Guid            ActionRecordId         { get; private set; }
    public string          TenantId               { get; private set; } = string.Empty;
    public Guid            RunId                  { get; private set; }
    public string          ActionType             { get; private set; } = string.Empty;
    public string          ProposedPayloadJson    { get; private set; } = string.Empty;
    public ActionStatus    Status                 { get; private set; }
    public RollbackStatus  RollbackStatus         { get; private set; }
    public string?         ExecutionPayloadJson   { get; private set; }
    public string?         OutcomeJson            { get; private set; }
    public string?         RollbackPayloadJson    { get; private set; }
    public string?         RollbackOutcomeJson    { get; private set; }
    public string?         ManualRollbackGuidance { get; private set; }
    public DateTimeOffset  CreatedAtUtc           { get; private set; }
    public DateTimeOffset? ExecutedAtUtc          { get; private set; }
    public DateTimeOffset? CompletedAtUtc         { get; private set; }
    public DateTimeOffset? RolledBackAtUtc        { get; private set; }

    // ─── Action lifecycle transitions ──────────────────────────────

    /// <summary>Approve the proposed action for execution.</summary>
    public void Approve()
    {
        if (Status is not ActionStatus.Proposed)
            throw new InvalidOperationException(
                "Only proposed actions can be approved.");
        Status = ActionStatus.Approved;
    }

    /// <summary>Reject the proposed action (terminal state).</summary>
    public void Reject()
    {
        if (Status is not ActionStatus.Proposed)
            throw new InvalidOperationException(
                "Only proposed actions can be rejected.");
        Status = ActionStatus.Rejected;
    }

    /// <summary>Mark the action as currently executing.</summary>
    public void MarkExecuting()
    {
        if (Status is not ActionStatus.Approved)
            throw new InvalidOperationException(
                "Only approved actions can be executed.");
        Status        = ActionStatus.Executing;
        ExecutedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Complete execution successfully.</summary>
    public void CompleteExecution(string executionPayloadJson, string outcomeJson)
    {
        if (Status is not ActionStatus.Executing)
            throw new InvalidOperationException(
                "Only executing actions can be completed.");
        Status               = ActionStatus.Completed;
        ExecutionPayloadJson = executionPayloadJson;
        OutcomeJson          = outcomeJson;
        CompletedAtUtc       = DateTimeOffset.UtcNow;
    }

    /// <summary>Record execution failure.</summary>
    public void FailExecution(string executionPayloadJson, string outcomeJson)
    {
        if (Status is not ActionStatus.Executing)
            throw new InvalidOperationException(
                "Only executing actions can be marked as failed.");
        Status               = ActionStatus.Failed;
        ExecutionPayloadJson = executionPayloadJson;
        OutcomeJson          = outcomeJson;
        CompletedAtUtc       = DateTimeOffset.UtcNow;
    }

    // ─── Rollback lifecycle transitions ────────────────────────────

    /// <summary>Request rollback for an executed action.</summary>
    public void RequestRollback()
    {
        if (Status is not (ActionStatus.Completed or ActionStatus.Failed))
            throw new InvalidOperationException(
                "Only completed or failed actions can be rolled back.");
        if (RollbackStatus is not Enums.RollbackStatus.Available)
            throw new InvalidOperationException(
                "Rollback is not available for this action.");
        RollbackStatus = Enums.RollbackStatus.Pending;
    }

    /// <summary>Approve the pending rollback for execution.</summary>
    public void ApproveRollback()
    {
        if (RollbackStatus is not Enums.RollbackStatus.Pending)
            throw new InvalidOperationException(
                "Only pending rollbacks can be approved.");
        RollbackStatus = Enums.RollbackStatus.Approved;
    }

    /// <summary>Complete rollback successfully.</summary>
    public void CompleteRollback(string rollbackOutcomeJson)
    {
        if (RollbackStatus is not Enums.RollbackStatus.Approved)
            throw new InvalidOperationException(
                "Only approved rollbacks can be completed.");
        RollbackStatus      = Enums.RollbackStatus.RolledBack;
        RollbackOutcomeJson = rollbackOutcomeJson;
        RolledBackAtUtc     = DateTimeOffset.UtcNow;
    }

    /// <summary>Record rollback failure.</summary>
    public void FailRollback(string rollbackOutcomeJson)
    {
        if (RollbackStatus is not Enums.RollbackStatus.Approved)
            throw new InvalidOperationException(
                "Only approved rollbacks can fail.");
        RollbackStatus      = Enums.RollbackStatus.RollbackFailed;
        RollbackOutcomeJson = rollbackOutcomeJson;
        RolledBackAtUtc     = DateTimeOffset.UtcNow;
    }
}
