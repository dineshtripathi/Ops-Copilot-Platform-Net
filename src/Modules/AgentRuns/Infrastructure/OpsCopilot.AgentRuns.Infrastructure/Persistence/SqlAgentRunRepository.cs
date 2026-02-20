using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Repositories;

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

    public async Task<AgentRun> CreateRunAsync(
        string tenantId, string alertFingerprint, CancellationToken ct = default)
    {
        var run = AgentRun.Create(tenantId, alertFingerprint);
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

    public async Task CompleteRunAsync(
        Guid runId, AgentRunStatus status,
        string summaryJson, string citationsJson, CancellationToken ct = default)
    {
        var run = await _db.AgentRuns.FindAsync([runId], ct)
                  ?? throw new InvalidOperationException($"AgentRun {runId} not found.");
        run.Complete(status, summaryJson, citationsJson);
        await _db.SaveChangesAsync(ct);
    }
}
