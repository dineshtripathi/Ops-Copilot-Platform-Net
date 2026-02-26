using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Domain;
using OpsCopilot.SafeActions.Domain.Entities;
using OpsCopilot.SafeActions.Domain.Enums;
using OpsCopilot.SafeActions.Domain.Repositories;

namespace OpsCopilot.SafeActions.Application.Orchestration;

/// <summary>
/// Orchestrates the safe action lifecycle: propose → approve → execute → rollback.
/// Enforces hard invariants:
///   §2.1.3 — Actions never execute silently (approval required).
///   §2.1.4 — Rollback requires separate approval and is fully auditable.
/// </summary>
public sealed class SafeActionOrchestrator
{
    private readonly IActionRecordRepository            _repository;
    private readonly IActionExecutor                    _executor;
    private readonly ISafeActionPolicy                  _policy;
    private readonly ITenantExecutionPolicy             _tenantExecutionPolicy;
    private readonly IActionTypeCatalog                 _catalog;
    private readonly ISafeActionsTelemetry              _telemetry;
    private readonly ILogger<SafeActionOrchestrator>    _logger;

    public SafeActionOrchestrator(
        IActionRecordRepository         repository,
        IActionExecutor                 executor,
        ISafeActionPolicy               policy,
        ITenantExecutionPolicy          tenantExecutionPolicy,
        IActionTypeCatalog              catalog,
        ISafeActionsTelemetry           telemetry,
        ILogger<SafeActionOrchestrator> logger)
    {
        _repository             = repository;
        _executor               = executor;
        _policy                 = policy;
        _tenantExecutionPolicy  = tenantExecutionPolicy;
        _catalog                = catalog;
        _telemetry              = telemetry;
        _logger                 = logger;
    }

    // ─── Propose ──────────────────────────────────────────────────

    public async Task<ActionRecord> ProposeAsync(
        string  tenantId,
        Guid    runId,
        string  actionType,
        string  proposedPayloadJson,
        string? rollbackPayloadJson,
        string? manualRollbackGuidance,
        CancellationToken ct = default)
    {
        // ── Catalog allowlist — unknown / disabled types are rejected before any policy check ───
        if (!_catalog.IsAllowlisted(actionType))
        {
            _telemetry.RecordPolicyDenied(actionType, tenantId);
            _logger.LogWarning(
                "Catalog denied action {ActionType} for tenant {TenantId}: action_type_not_allowed",
                actionType, tenantId);
            throw new PolicyDeniedException(
                "action_type_not_allowed",
                $"Action type '{actionType}' is not enabled in the catalog.");
        }

        // ── Policy gate — denials never create a database row ───
        var decision = _policy.Evaluate(tenantId, actionType);
        if (!decision.Allowed)
        {
            _telemetry.RecordPolicyDenied(actionType, tenantId);
            _logger.LogWarning(
                "Policy denied action {ActionType} for tenant {TenantId}: {ReasonCode}",
                actionType, tenantId, decision.ReasonCode);
            throw new PolicyDeniedException(decision.ReasonCode, decision.Message);
        }

        _logger.LogInformation(
            "Proposing action {ActionType} for tenant {TenantId}, run {RunId}",
            actionType, tenantId, runId);

        var record = await _repository.CreateActionRecordAsync(
            tenantId, runId, actionType, proposedPayloadJson,
            rollbackPayloadJson, manualRollbackGuidance, ct);

        _logger.LogInformation(
            "Action {ActionRecordId} proposed (type={ActionType}, rollback={RollbackStatus})",
            record.ActionRecordId, actionType, record.RollbackStatus);

        return record;
    }

    // ─── Approve ──────────────────────────────────────────────────

    public async Task<ActionRecord> ApproveAsync(
        Guid   actionRecordId,
        string approverIdentity,
        string reason,
        CancellationToken ct = default)
    {
        var record = await GetRequiredAsync(actionRecordId, ct);

        record.Approve();

        var approval = ApprovalRecord.Create(
            actionRecordId, approverIdentity,
            ApprovalDecision.Approved, reason, "Action");

        await _repository.AppendApprovalAsync(approval, ct);
        await _repository.SaveAsync(record, ct);

        _logger.LogInformation(
            "Action {ActionRecordId} approved by {Approver}",
            actionRecordId, approverIdentity);

        return record;
    }

    // ─── Reject ───────────────────────────────────────────────────

    public async Task<ActionRecord> RejectAsync(
        Guid   actionRecordId,
        string approverIdentity,
        string reason,
        CancellationToken ct = default)
    {
        var record = await GetRequiredAsync(actionRecordId, ct);

        record.Reject();

        var approval = ApprovalRecord.Create(
            actionRecordId, approverIdentity,
            ApprovalDecision.Rejected, reason, "Action");

        await _repository.AppendApprovalAsync(approval, ct);
        await _repository.SaveAsync(record, ct);

        _logger.LogInformation(
            "Action {ActionRecordId} rejected by {Approver}",
            actionRecordId, approverIdentity);

        return record;
    }

    // ─── Execute ──────────────────────────────────────────────────

    public async Task<ActionRecord> ExecuteAsync(
        Guid actionRecordId,
        CancellationToken ct = default)
    {
        var record = await GetRequiredAsync(actionRecordId, ct);
        _telemetry.RecordExecutionAttempt(record.ActionType, record.TenantId);

        // ── Tenant execution policy gate ────────────────────────
        var tenantDecision = _tenantExecutionPolicy.EvaluateExecution(
            record.TenantId, record.ActionType);
        if (!tenantDecision.Allowed)
        {
            _telemetry.RecordPolicyDenied(record.ActionType, record.TenantId);
            _logger.LogWarning(
                "Tenant execution policy denied execute for action {ActionRecordId} "
                + "(type={ActionType}, tenant={TenantId}): {ReasonCode}",
                actionRecordId, record.ActionType, record.TenantId,
                tenantDecision.ReasonCode);
            throw new PolicyDeniedException(
                tenantDecision.ReasonCode, tenantDecision.Message);
        }

        // ── Replay guard — only Approved records may begin execution ──
        if (record.Status is not ActionStatus.Approved)
        {
            _telemetry.RecordReplayConflict(record.ActionType);
            _logger.LogWarning(
                "Execute replay blocked for action {ActionRecordId} — current status {Status} is not Approved",
                actionRecordId, record.Status);
            throw new InvalidOperationException(
                $"Action {actionRecordId} cannot be executed because its status is {record.Status}.");
        }

        // Transition to Executing (enforces approval-required invariant §2.1.3)
        record.MarkExecuting();
        await _repository.SaveAsync(record, ct);

        _logger.LogInformation(
            "Executing action {ActionRecordId} (type={ActionType}, tenant={TenantId}) — transition Approved→Executing",
            actionRecordId, record.ActionType, record.TenantId);

        var sw = Stopwatch.StartNew();
        ActionExecutionResult result;
        try
        {
            result = await _executor.ExecuteAsync(
                record.ActionType, record.ProposedPayloadJson, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Action {ActionRecordId} execution threw exception (type={ActionType}, tenant={TenantId}, durationMs={DurationMs})",
                actionRecordId, record.ActionType, record.TenantId, sw.ElapsedMilliseconds);
            result = new ActionExecutionResult(
                Success:      false,
                ResponseJson: $"{{\"error\":\"{ex.Message}\"}}",
                DurationMs:   sw.ElapsedMilliseconds);
        }
        sw.Stop();

        // Record execution log (append-only audit)
        var log = ExecutionLog.Create(
            actionRecordId, "Execute", record.ProposedPayloadJson,
            result.ResponseJson, result.Success ? "Success" : "Failed",
            result.DurationMs > 0 ? result.DurationMs : sw.ElapsedMilliseconds);

        await _repository.AppendExecutionLogAsync(log, ct);

        // Transition to terminal state
        if (result.Success)
        {
            record.CompleteExecution(record.ProposedPayloadJson, result.ResponseJson);
            _telemetry.RecordExecutionSuccess(record.ActionType, record.TenantId);
        }
        else
        {
            record.FailExecution(record.ProposedPayloadJson, result.ResponseJson);
            _telemetry.RecordExecutionFailure(record.ActionType, record.TenantId);
        }

        await _repository.SaveAsync(record, ct);

        var executeStatus = result.Success ? "Completed" : "Failed";
        _logger.LogInformation(
            "Action {ActionRecordId} execution {Status} (type={ActionType}, tenant={TenantId}, durationMs={DurationMs}, transition=Executing→{Status})",
            actionRecordId, executeStatus, record.ActionType, record.TenantId, sw.ElapsedMilliseconds, executeStatus);

        return record;
    }

    // ─── Rollback lifecycle ───────────────────────────────────────

    public async Task<ActionRecord> RequestRollbackAsync(
        Guid actionRecordId,
        CancellationToken ct = default)
    {
        var record = await GetRequiredAsync(actionRecordId, ct);

        record.RequestRollback();
        await _repository.SaveAsync(record, ct);

        _logger.LogInformation(
            "Rollback requested for action {ActionRecordId}", actionRecordId);

        return record;
    }

    public async Task<ActionRecord> ApproveRollbackAsync(
        Guid   actionRecordId,
        string approverIdentity,
        string reason,
        CancellationToken ct = default)
    {
        var record = await GetRequiredAsync(actionRecordId, ct);

        record.ApproveRollback();

        var approval = ApprovalRecord.Create(
            actionRecordId, approverIdentity,
            ApprovalDecision.Approved, reason, "Rollback");

        await _repository.AppendApprovalAsync(approval, ct);
        await _repository.SaveAsync(record, ct);

        _logger.LogInformation(
            "Rollback approved for action {ActionRecordId} by {Approver}",
            actionRecordId, approverIdentity);

        return record;
    }

    public async Task<ActionRecord> ExecuteRollbackAsync(
        Guid actionRecordId,
        CancellationToken ct = default)
    {
        var record = await GetRequiredAsync(actionRecordId, ct);
        _telemetry.RecordExecutionAttempt(record.ActionType, record.TenantId);

        // ── Tenant execution policy gate ────────────────────────
        var tenantDecision = _tenantExecutionPolicy.EvaluateExecution(
            record.TenantId, record.ActionType);
        if (!tenantDecision.Allowed)
        {
            _telemetry.RecordPolicyDenied(record.ActionType, record.TenantId);
            _logger.LogWarning(
                "Tenant execution policy denied rollback-execute for action {ActionRecordId} "
                + "(type={ActionType}, tenant={TenantId}): {ReasonCode}",
                actionRecordId, record.ActionType, record.TenantId,
                tenantDecision.ReasonCode);
            throw new PolicyDeniedException(
                tenantDecision.ReasonCode, tenantDecision.Message);
        }

        if (record.RollbackPayloadJson is null)
            throw new InvalidOperationException(
                $"Action {actionRecordId} has no rollback payload.");

        // ── Replay guard — only RollbackApproved records may begin rollback execution ──
        if (record.RollbackStatus is not RollbackStatus.Approved)
        {
            _telemetry.RecordReplayConflict(record.ActionType);
            _logger.LogWarning(
                "Rollback replay blocked for action {ActionRecordId} — current rollback status {RollbackStatus} is not Approved",
                actionRecordId, record.RollbackStatus);
            throw new InvalidOperationException(
                $"Action {actionRecordId} cannot execute rollback because its rollback status is {record.RollbackStatus}.");
        }

        _logger.LogInformation(
            "Executing rollback for action {ActionRecordId} (type={ActionType}, tenant={TenantId}) — transition RollbackApproved→RollingBack",
            actionRecordId, record.ActionType, record.TenantId);

        var sw = Stopwatch.StartNew();
        ActionExecutionResult result;
        try
        {
            result = await _executor.RollbackAsync(
                record.ActionType, record.RollbackPayloadJson, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Rollback for action {ActionRecordId} threw exception (type={ActionType}, tenant={TenantId}, durationMs={DurationMs})",
                actionRecordId, record.ActionType, record.TenantId, sw.ElapsedMilliseconds);
            result = new ActionExecutionResult(
                Success:      false,
                ResponseJson: $"{{\"error\":\"{ex.Message}\"}}",
                DurationMs:   sw.ElapsedMilliseconds);
        }
        sw.Stop();

        var log = ExecutionLog.Create(
            actionRecordId, "Rollback", record.RollbackPayloadJson,
            result.ResponseJson, result.Success ? "Success" : "Failed",
            result.DurationMs > 0 ? result.DurationMs : sw.ElapsedMilliseconds);

        await _repository.AppendExecutionLogAsync(log, ct);

        if (result.Success)
        {
            record.CompleteRollback(result.ResponseJson);
            _telemetry.RecordExecutionSuccess(record.ActionType, record.TenantId);
        }
        else
        {
            record.FailRollback(result.ResponseJson);
            _telemetry.RecordExecutionFailure(record.ActionType, record.TenantId);
        }

        await _repository.SaveAsync(record, ct);

        var rollbackStatus = result.Success ? "RolledBack" : "RollbackFailed";
        _logger.LogInformation(
            "Rollback for action {ActionRecordId} {Status} (type={ActionType}, tenant={TenantId}, durationMs={DurationMs}, transition=RollingBack→{Status})",
            actionRecordId, rollbackStatus, record.ActionType, record.TenantId, sw.ElapsedMilliseconds, rollbackStatus);

        return record;
    }

    // ─── Queries ──────────────────────────────────────────────────

    public async Task<ActionRecord?> GetAsync(
        Guid actionRecordId, CancellationToken ct = default)
        => await _repository.GetByIdAsync(actionRecordId, ct);

    public async Task<IReadOnlyList<ActionRecord>> ListByTenantAsync(
        string tenantId, int limit, CancellationToken ct = default)
        => await _repository.GetByTenantAsync(tenantId, limit, ct);

    public async Task<IReadOnlyList<ActionRecord>> ListByRunAsync(
        Guid runId, CancellationToken ct = default)
        => await _repository.GetByRunIdAsync(runId, ct);

    public async Task<IReadOnlyList<ActionRecord>> QueryByTenantAsync(
        string          tenantId,
        ActionStatus?   status,
        RollbackStatus? rollbackStatus,
        string?         actionType,
        bool?           hasExecutionLogs,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int             limit,
        CancellationToken ct = default)
        => await _repository.QueryByTenantAsync(
            tenantId, status, rollbackStatus, actionType,
            hasExecutionLogs, fromUtc, toUtc, limit, ct);

    public async Task<IReadOnlyDictionary<Guid, AuditSummary>> GetAuditSummariesAsync(
        IReadOnlyList<Guid> actionRecordIds, CancellationToken ct = default)
        => await _repository.GetAuditSummariesAsync(actionRecordIds, ct);

    public async Task<IReadOnlyList<ApprovalRecord>> GetApprovalsForActionAsync(
        Guid actionRecordId, CancellationToken ct = default)
        => await _repository.GetApprovalsForActionAsync(actionRecordId, ct);

    public async Task<IReadOnlyList<ExecutionLog>> GetExecutionLogsForActionAsync(
        Guid actionRecordId, CancellationToken ct = default)
        => await _repository.GetExecutionLogsForActionAsync(actionRecordId, ct);

    // ─── Helpers ──────────────────────────────────────────────────

    private async Task<ActionRecord> GetRequiredAsync(
        Guid actionRecordId, CancellationToken ct)
        => await _repository.GetByIdAsync(actionRecordId, ct)
           ?? throw new KeyNotFoundException(
               $"Action record {actionRecordId} not found.");
}
