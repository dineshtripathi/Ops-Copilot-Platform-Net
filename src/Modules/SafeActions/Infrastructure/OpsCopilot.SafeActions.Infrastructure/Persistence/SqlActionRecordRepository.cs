using Microsoft.EntityFrameworkCore;
using OpsCopilot.SafeActions.Domain.Entities;
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
}
