using OpsCopilot.AgentRuns.Application.Abstractions;

namespace OpsCopilot.AgentRuns.Application.Services;

/// <summary>No-op memory service — default when incident recall is not configured.</summary>
internal sealed class NullIncidentMemoryService : IIncidentMemoryService
{
    public Task<IReadOnlyList<MemoryCitation>> RecallAsync(
        string alertFingerprint, string tenantId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MemoryCitation>>(Array.Empty<MemoryCitation>());
}
