namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>
/// Port for indexing completed triage runs into vector memory.
/// AgentRuns.Application does not depend on Rag.Domain; it passes primitives only.
/// Infrastructure provides the concrete implementation (null no-op by default;
/// <see cref="RagBackedIncidentMemoryIndexer"/> when incident recall is enabled).
/// </summary>
public interface IIncidentMemoryIndexer
{
    /// <summary>
    /// Indexes the completed run summary so it can be recalled in future triage sessions.
    /// Implementations MUST be idempotent (upsert semantics).
    /// </summary>
    Task IndexAsync(
        Guid           runId,
        string         tenantId,
        string         alertFingerprint,
        string         summaryText,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default);
}
