using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Models;
using OpsCopilot.AgentRuns.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace OpsCopilot.AgentRuns.Infrastructure.Persistence;

/// <summary>
/// SQL Server–backed implementation of <see cref="IAgentRunRepository"/>.
/// Enforces the append-only contract:
///   - CreateRunAsync / AppendToolCallAsync → INSERT only.
///   - CompleteRunAsync                    → UPDATE exactly four columns.
/// </summary>
public sealed class SqlAgentRunRepository : IAgentRunRepository
{
    private readonly AgentRunsDbContext _db;

    public SqlAgentRunRepository(AgentRunsDbContext db) => _db = db;

    public Task<AgentRun> CreateRunAsync(
        string tenantId,
        string alertFingerprint,
        Guid? sessionId = null,
        CancellationToken ct = default)
        => CreateRunAsync(tenantId, alertFingerprint, sessionId, context: null, ct);

    public async Task<AgentRun> CreateRunAsync(
        string tenantId,
        string alertFingerprint,
        Guid? sessionId = null,
        RunContext? context = null,
        CancellationToken ct = default)
    {
        var run = AgentRun.Create(tenantId, alertFingerprint, sessionId, context);
        _db.AgentRuns.Add(run);
        await _db.SaveChangesAsync(ct);
        return run;
    }

    /// <summary>INSERT-only. Never updates an existing ToolCall row.</summary>
    public async Task AppendToolCallAsync(ToolCall toolCall, CancellationToken ct = default)
    {
        _db.ToolCalls.Add(toolCall);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>INSERT-only. Never updates an existing PolicyEvent row.</summary>
    public async Task AppendPolicyEventAsync(AgentRunPolicyEvent policyEvent, CancellationToken ct = default)
    {
        _db.PolicyEvents.Add(policyEvent);
        await _db.SaveChangesAsync(ct);
    }

    public async Task CompleteRunAsync(
        Guid runId, AgentRunStatus status,
        string summaryJson, string citationsJson, CancellationToken ct = default)
    {
        var run = await _db.AgentRuns.FindAsync([runId], ct)
                  ?? throw new InvalidOperationException($"AgentRun {runId} not found.");
        run.Complete(status, summaryJson, citationsJson);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentRun>> GetRecentRunsBySessionAsync(
        Guid sessionId, int limit, CancellationToken ct = default)
    {
        return await _db.AgentRuns
            .Where(r => r.SessionId == sessionId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Stub — token usage persistence is not wired in this slice.
    /// The method is intentionally a no-op; rows will be updated in a future slice.
    /// </remarks>
    public Task UpdateTokenUsageAsync(
        Guid runId, int inputTokens, int outputTokens, int totalTokens,
        CancellationToken ct = default)
        => Task.CompletedTask;

    public async Task UpdateRunLedgerAsync(
        Guid runId, string modelId, string? promptVersionId,
        int inputTokens, int outputTokens, int totalTokens, decimal estimatedCost,
        CancellationToken ct = default)
    {
        var run = await _db.AgentRuns.FindAsync([runId], ct)
                  ?? throw new InvalidOperationException($"AgentRun {runId} not found.");
        run.SetLedgerMetadata(modelId, promptVersionId, inputTokens, outputTokens, totalTokens, estimatedCost);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<AgentRun?> FindRecentRunAsync(
        string tenantId,
        string alertFingerprint,
        int    windowMinutes,
        CancellationToken ct = default)
    {
        var since = DateTimeOffset.UtcNow.AddMinutes(-windowMinutes);
        return await _db.AgentRuns
            .Where(r => r.TenantId        == tenantId
                     && r.AlertFingerprint == alertFingerprint
                     && (r.Status == AgentRunStatus.Pending
                         || (r.Status == AgentRunStatus.Completed && r.CompletedAtUtc >= since)))
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public async Task<AgentRunFeedback> SaveFeedbackAsync(
        Guid    runId,
        string  tenantId,
        int     rating,
        string? comment,
        CancellationToken ct = default)
    {
        // Guard: run must exist and belong to this tenant.
        var run = await _db.AgentRuns.FindAsync([runId], ct)
                  ?? throw new InvalidOperationException($"AgentRun {runId} not found.");

        if (!string.Equals(run.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"AgentRun {runId} does not belong to tenant '{tenantId}'.");

        var feedback = AgentRunFeedback.Create(runId, tenantId, rating, comment);
        _db.Feedbacks.Add(feedback);
        await _db.SaveChangesAsync(ct);
        return feedback;
    }

    /// <inheritdoc />
    public Task<bool> FeedbackExistsAsync(Guid runId, CancellationToken ct = default)
        => _db.Feedbacks.AnyAsync(f => f.RunId == runId, ct);
}
