using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Models;

namespace OpsCopilot.AgentRuns.Domain.Repositories;

/// <summary>
/// Persistence contract for AgentRun aggregate.
/// 
/// Append-only rules enforced here:
///   - CreateRunAsync   → INSERT a new AgentRun row (Pending).
///   - AppendToolCallAsync → INSERT a new ToolCall row (never update).
///   - CompleteRunAsync → UPDATE only Status + CompletedAtUtc + SummaryJson + CitationsJson.
/// </summary>
public interface IAgentRunRepository
{
    Task<AgentRun> CreateRunAsync(
        string tenantId,
        string alertFingerprint,
        Guid? sessionId = null,
        CancellationToken ct = default);

    Task<AgentRun> CreateRunAsync(
        string tenantId,
        string alertFingerprint,
        Guid? sessionId = null,
        RunContext? context = null,
        CancellationToken ct = default);

    /// <summary>Inserts the tool-call row. Never updates an existing row.</summary>
    Task AppendToolCallAsync(ToolCall toolCall, CancellationToken ct = default);

    /// <summary>Inserts the policy-event row. Never updates an existing row.</summary>
    Task AppendPolicyEventAsync(AgentRunPolicyEvent policyEvent, CancellationToken ct = default);

    /// <summary>Updates only the terminal fields of the run. May only be called once per run.</summary>
    Task CompleteRunAsync(
        Guid          runId,
        AgentRunStatus status,
        string        summaryJson,
        string        citationsJson,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent completed runs for the given session,
    /// ordered by <c>CreatedAtUtc</c> descending, capped at <paramref name="limit"/>.
    /// Used to build compact session context on resume.
    /// </summary>
    Task<IReadOnlyList<AgentRun>> GetRecentRunsBySessionAsync(
        Guid sessionId, int limit, CancellationToken ct = default);

    /// <summary>Records LLM token usage for a completed run. Idempotent if called more than once.</summary>
    Task UpdateTokenUsageAsync(
        Guid runId, int inputTokens, int outputTokens, int totalTokens,
        CancellationToken ct = default);

    /// <summary>Persists LLM ledger metadata (model, prompt version, tokens, cost) for a completed run.</summary>
    Task UpdateRunLedgerAsync(
        Guid     runId,
        string   modelId,
        string?  promptVersionId,
        int      inputTokens,
        int      outputTokens,
        int      totalTokens,
        decimal  estimatedCost,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent Pending or recently-Completed run for the given
    /// (tenantId, alertFingerprint) within <paramref name="windowMinutes"/>.
    /// Used by the idempotency guard to avoid duplicate triage runs for the same alert.
    /// Returns <c>null</c> when no qualifying run exists.
    /// </summary>
    Task<AgentRun?> FindRecentRunAsync(
        string tenantId,
        string alertFingerprint,
        int    windowMinutes,
        CancellationToken ct = default);

    /// <summary>
    /// Slice 123: Persists operator feedback for a completed run (INSERT-only).
    /// Returns the new feedback record. Throws <see cref="InvalidOperationException"/>
    /// when the runId does not exist or does not belong to tenantId.
    /// </summary>
    Task<AgentRunFeedback> SaveFeedbackAsync(
        Guid    runId,
        string  tenantId,
        int     rating,
        string? comment,
        CancellationToken ct = default);

    /// <summary>
    /// Slice 123: Returns true when at least one feedback record already exists
    /// for the supplied runId, regardless of tenant.
    /// </summary>
    Task<bool> FeedbackExistsAsync(Guid runId, CancellationToken ct = default);

    /// <summary>
    /// Slice 127: Loads a single AgentRun by primary key.
    /// Returns <c>null</c> when the run does not exist or does not belong to <paramref name="tenantId"/>.
    /// </summary>
    Task<AgentRun?> GetByRunIdAsync(Guid runId, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Slice 128: Transitions a run from <see cref="AgentRunStatus.Pending"/> to
    /// <see cref="AgentRunStatus.Running"/> to signal the triage pipeline has started.
    /// No-op when the run is already in a non-Pending state (idempotent).
    /// </summary>
    Task MarkRunningAsync(Guid runId, CancellationToken ct = default);

    /// <summary>
    /// Slice 130: Returns all runs currently in <see cref="AgentRunStatus.Running"/> whose
    /// <c>CreatedAtUtc</c> is older than <paramref name="createdBefore"/>.
    /// Used by the stuck-run watchdog to find runs that were never driven to a terminal state
    /// (e.g. because the server process restarted mid-triage).
    /// </summary>
    Task<IReadOnlyList<AgentRun>> GetStuckRunsAsync(
        DateTimeOffset createdBefore,
        CancellationToken ct = default);
}
