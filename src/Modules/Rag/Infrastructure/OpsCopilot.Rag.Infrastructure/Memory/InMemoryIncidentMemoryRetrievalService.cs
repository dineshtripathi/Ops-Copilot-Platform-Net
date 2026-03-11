using OpsCopilot.Rag.Application.Memory;

namespace OpsCopilot.Rag.Infrastructure.Memory;

/// <summary>No-op retrieval — default registration when vector store is not configured.</summary>
internal sealed class InMemoryIncidentMemoryRetrievalService : IIncidentMemoryRetrievalService
{
    public Task<IReadOnlyList<IncidentMemoryHit>> SearchAsync(
        IncidentMemoryQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<IncidentMemoryHit>>(Array.Empty<IncidentMemoryHit>());
}
