using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.Rag.Domain;
using RagIndexer = OpsCopilot.Rag.Application.Memory.IIncidentMemoryIndexer;

namespace OpsCopilot.AgentRuns.Infrastructure.Memory;

/// <summary>
/// Anti-corruption bridge: converts the AgentRuns primitive arguments into a
/// <see cref="IncidentMemoryDocument"/> and delegates to the Rag module's indexer.
/// </summary>
internal sealed class RagBackedIncidentMemoryIndexer : IIncidentMemoryIndexer
{
    private readonly RagIndexer _inner;

    public RagBackedIncidentMemoryIndexer(RagIndexer inner) => _inner = inner;

    public Task IndexAsync(
        Guid           runId,
        string         tenantId,
        string         alertFingerprint,
        string         summaryText,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        var document = new IncidentMemoryDocument
        {
            Id               = Guid.NewGuid(),
            TenantId         = tenantId,
            AlertFingerprint = alertFingerprint,
            RunId            = runId.ToString(),
            SummaryText      = summaryText,
            CreatedAtUtc     = createdAtUtc,
        };
        return _inner.IndexAsync(document, cancellationToken);
    }
}
