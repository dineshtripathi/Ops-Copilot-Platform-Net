using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;

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
        CancellationToken ct = default);

    /// <summary>Inserts the tool-call row. Never updates an existing row.</summary>
    Task AppendToolCallAsync(ToolCall toolCall, CancellationToken ct = default);

    /// <summary>Updates only the terminal fields of the run. May only be called once per run.</summary>
    Task CompleteRunAsync(
        Guid          runId,
        AgentRunStatus status,
        string        summaryJson,
        string        citationsJson,
        CancellationToken ct = default);
}
