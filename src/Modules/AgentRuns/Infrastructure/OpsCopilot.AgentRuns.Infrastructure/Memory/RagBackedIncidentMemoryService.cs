using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.Rag.Application.Memory;

namespace OpsCopilot.AgentRuns.Infrastructure.Memory;

internal sealed class RagBackedIncidentMemoryService : IIncidentMemoryService
{
    private readonly IIncidentMemoryRetrievalService _retrieval;

    public RagBackedIncidentMemoryService(IIncidentMemoryRetrievalService retrieval)
        => _retrieval = retrieval;

    public async Task<IReadOnlyList<MemoryCitation>> RecallAsync(
        string alertFingerprint, string tenantId, CancellationToken cancellationToken = default)
    {
        var query = new IncidentMemoryQuery(alertFingerprint, tenantId);
        var hits  = await _retrieval.SearchAsync(query, cancellationToken).ConfigureAwait(false);

        return hits
            .Select(h => new MemoryCitation(
                h.RunId,
                h.AlertFingerprint,
                h.SummarySnippet,
                h.Score,
                h.CreatedAtUtc))
            .ToList();
    }
}
