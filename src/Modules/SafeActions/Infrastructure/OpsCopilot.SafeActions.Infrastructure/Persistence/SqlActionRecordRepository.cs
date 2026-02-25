using Microsoft.EntityFrameworkCore;
using OpsCopilot.SafeActions.Domain;
using OpsCopilot.SafeActions.Domain.Entities;
using OpsCopilot.SafeActions.Domain.Enums;
using OpsCopilot.SafeActions.Domain.Repositories;

namespace OpsCopilot.SafeActions.Infrastructure.Persistence;

internal sealed class SqlActionRecordRepository : IActionRecordRepository
{
    private readonly SafeActionsDbContext _db;

    public SqlActionRecordRepository(SafeActionsDbContext db) => _db = db;

    public async Task<ActionRecord> CreateActionRecordAsync(
        string  tenantId,
        Guid    runId,
        string  actionType,
        string  proposedPayloadJson,
        string? rollbackPayloadJson,
        string? manualRollbackGuidance,
        CancellationToken ct)
    {
        var record = ActionRecord.Create(
            tenantId, runId, actionType, proposedPayloadJson,
            rollbackPayloadJson, manualRollbackGuidance);

        _db.ActionRecords.Add(record);
        await _db.SaveChangesAsync(ct);
        return record;
    }

    public async Task<ActionRecord?> GetByIdAsync(
        Guid actionRecordId, CancellationToken ct)
        => await _db.ActionRecords.FindAsync(new object[] { actionRecordId }, ct);

    public async Task SaveAsync(ActionRecord record, CancellationToken ct)
        => await _db.SaveChangesAsync(ct);

    public async Task AppendApprovalAsync(ApprovalRecord approval, CancellationToken ct)
    {
        _db.ApprovalRecords.Add(approval);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AppendExecutionLogAsync(ExecutionLog log, CancellationToken ct)
    {
        _db.ExecutionLogs.Add(log);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ActionRecord>> GetByTenantAsync(
        string tenantId, int limit, CancellationToken ct)
        => await _db.ActionRecords
            .Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ActionRecord>> GetByRunIdAsync(
        Guid runId, CancellationToken ct)
        => await _db.ActionRecords
            .Where(r => r.RunId == runId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ActionRecord>> QueryByTenantAsync(
        string          tenantId,
        ActionStatus?   status,
        RollbackStatus? rollbackStatus,
        string?         actionType,
        bool?           hasExecutionLogs,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int             limit,
        CancellationToken ct)
    {
        var query = _db.ActionRecords
            .Where(r => r.TenantId == tenantId);

        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        if (rollbackStatus.HasValue)
            query = query.Where(r => r.RollbackStatus == rollbackStatus.Value);

        if (!string.IsNullOrWhiteSpace(actionType))
            query = query.Where(r => r.ActionType == actionType);

        if (hasExecutionLogs == true)
            query = query.Where(r => _db.ExecutionLogs.Any(e => e.ActionRecordId == r.ActionRecordId));
        else if (hasExecutionLogs == false)
            query = query.Where(r => !_db.ExecutionLogs.Any(e => e.ActionRecordId == r.ActionRecordId));

        if (fromUtc.HasValue)
            query = query.Where(r => r.CreatedAtUtc >= fromUtc.Value);

        if (toUtc.HasValue)
            query = query.Where(r => r.CreatedAtUtc < toUtc.Value);

        return await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, AuditSummary>> GetAuditSummariesAsync(
        IReadOnlyList<Guid> actionRecordIds, CancellationToken ct)
    {
        if (actionRecordIds.Count == 0)
            return new Dictionary<Guid, AuditSummary>();

        var ids = actionRecordIds.ToHashSet();

        var execLogs = await _db.ExecutionLogs
            .Where(e => ids.Contains(e.ActionRecordId))
            .ToListAsync(ct);

        var approvals = await _db.ApprovalRecords
            .Where(a => ids.Contains(a.ActionRecordId))
            .ToListAsync(ct);

        var execByRecord = execLogs
            .GroupBy(e => e.ActionRecordId)
            .ToDictionary(g => g.Key, g =>
            {
                var last = g.OrderByDescending(e => e.ExecutedAtUtc).First();
                return (Count: g.Count(), LastAtUtc: last.ExecutedAtUtc, LastSuccess: last.Status == "Success");
            });

        var approvalsByRecord = approvals
            .GroupBy(a => a.ActionRecordId)
            .ToDictionary(g => g.Key, g =>
            {
                var last = g.OrderByDescending(a => a.CreatedAtUtc).First();
                return (Count: g.Count(), LastDecision: last.Decision.ToString(), LastAtUtc: last.CreatedAtUtc);
            });

        var result = new Dictionary<Guid, AuditSummary>();

        foreach (var id in ids)
        {
            var hasExec = execByRecord.TryGetValue(id, out var exec);
            var hasAppr = approvalsByRecord.TryGetValue(id, out var appr);

            if (!hasExec && !hasAppr) continue;

            result[id] = new AuditSummary(
                ExecutionLogCount:    hasExec ? exec.Count : 0,
                LastExecutionAtUtc:   hasExec ? exec.LastAtUtc : null,
                LastExecutionSuccess: hasExec ? exec.LastSuccess : null,
                ApprovalCount:        hasAppr ? appr.Count : 0,
                LastApprovalDecision: hasAppr ? appr.LastDecision : null,
                LastApprovalAtUtc:    hasAppr ? appr.LastAtUtc : null);
        }

        return result;
    }

    public async Task<IReadOnlyList<ApprovalRecord>> GetApprovalsForActionAsync(
        Guid actionRecordId, CancellationToken ct = default) =>
        await _db.ApprovalRecords
            .Where(a => a.ActionRecordId == actionRecordId)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ExecutionLog>> GetExecutionLogsForActionAsync(
        Guid actionRecordId, CancellationToken ct = default) =>
        await _db.ExecutionLogs
            .Where(e => e.ActionRecordId == actionRecordId)
            .OrderBy(e => e.ExecutedAtUtc)
            .ToListAsync(ct);
}
