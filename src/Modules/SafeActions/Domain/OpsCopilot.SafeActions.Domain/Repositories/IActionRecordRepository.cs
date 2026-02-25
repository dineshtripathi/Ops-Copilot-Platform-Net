using OpsCopilot.SafeActions.Domain.Entities;
using OpsCopilot.SafeActions.Domain.Enums;

namespace OpsCopilot.SafeActions.Domain.Repositories;

/// <summary>
/// Persistence contract for action records.
/// Follows append-only pattern for audit records (ApprovalRecord, ExecutionLog).
/// ActionRecord status transitions are applied on the loaded entity and saved via SaveAsync.
/// </summary>
public interface IActionRecordRepository
{
    /// <summary>Creates a new action record in Proposed status.</summary>
    Task<ActionRecord> CreateActionRecordAsync(
        string  tenantId,
        Guid    runId,
        string  actionType,
        string  proposedPayloadJson,
        string? rollbackPayloadJson,
        string? manualRollbackGuidance,
        CancellationToken ct = default);

    /// <summary>Loads an action record by ID (returns null if not found).</summary>
    Task<ActionRecord?> GetByIdAsync(Guid actionRecordId, CancellationToken ct = default);

    /// <summary>Persists changes to a tracked action record entity.</summary>
    Task SaveAsync(ActionRecord record, CancellationToken ct = default);

    /// <summary>Appends an immutable approval record.</summary>
    Task AppendApprovalAsync(ApprovalRecord approval, CancellationToken ct = default);

    /// <summary>Appends an immutable execution log entry.</summary>
    Task AppendExecutionLogAsync(ExecutionLog log, CancellationToken ct = default);

    /// <summary>Returns recent action records for a tenant, ordered by creation date descending.</summary>
    Task<IReadOnlyList<ActionRecord>> GetByTenantAsync(
        string tenantId, int limit, CancellationToken ct = default);

    /// <summary>Returns action records associated with a specific agent run.</summary>
    Task<IReadOnlyList<ActionRecord>> GetByRunIdAsync(
        Guid runId, CancellationToken ct = default);

    /// <summary>
    /// Returns action records for a tenant matching the supplied filters,
    /// ordered by creation date descending.
    /// </summary>
    Task<IReadOnlyList<ActionRecord>> QueryByTenantAsync(
        string          tenantId,
        ActionStatus?   status,
        RollbackStatus? rollbackStatus,
        string?         actionType,
        bool?           hasExecutionLogs,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int             limit,
        CancellationToken ct = default);

    /// <summary>
    /// Returns audit summaries (execution log + approval aggregates) for a batch
    /// of action record IDs. IDs with no audit data are absent from the dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, AuditSummary>> GetAuditSummariesAsync(
        IReadOnlyList<Guid> actionRecordIds, CancellationToken ct = default);

    /// <summary>Returns all approval records for a single action, ordered by creation date ascending.</summary>
    Task<IReadOnlyList<ApprovalRecord>> GetApprovalsForActionAsync(
        Guid actionRecordId, CancellationToken ct = default);

    /// <summary>Returns all execution log entries for a single action, ordered by execution date ascending.</summary>
    Task<IReadOnlyList<ExecutionLog>> GetExecutionLogsForActionAsync(
        Guid actionRecordId, CancellationToken ct = default);
}
