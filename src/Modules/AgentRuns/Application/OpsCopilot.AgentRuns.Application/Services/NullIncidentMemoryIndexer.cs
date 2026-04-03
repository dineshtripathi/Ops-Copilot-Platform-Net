using OpsCopilot.AgentRuns.Application.Abstractions;

namespace OpsCopilot.AgentRuns.Application.Services;

/// <summary>No-op indexer — default when incident recall / vector memory is not configured.</summary>
internal sealed class NullIncidentMemoryIndexer : IIncidentMemoryIndexer
{
    public Task IndexAsync(
        Guid           runId,
        string         tenantId,
        string         alertFingerprint,
        string         summaryText,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
