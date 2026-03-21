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
}
