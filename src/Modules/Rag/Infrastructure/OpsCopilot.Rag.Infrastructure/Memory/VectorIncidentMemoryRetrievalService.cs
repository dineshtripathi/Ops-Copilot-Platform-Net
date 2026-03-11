using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using OpsCopilot.Rag.Application.Memory;
using OpsCopilot.Rag.Domain;

namespace OpsCopilot.Rag.Infrastructure.Memory;

internal sealed class VectorIncidentMemoryRetrievalService : IIncidentMemoryRetrievalService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>>          _embedder;
    private readonly VectorStoreCollection<Guid, IncidentMemoryDocument>   _collection;
    private readonly ILogger<VectorIncidentMemoryRetrievalService>          _logger;

    public VectorIncidentMemoryRetrievalService(
        IEmbeddingGenerator<string, Embedding<float>>            embedder,
        VectorStoreCollection<Guid, IncidentMemoryDocument>      collection,
        ILogger<VectorIncidentMemoryRetrievalService>            logger)
    {
        _embedder   = embedder;
        _collection = collection;
        _logger     = logger;
    }

    public async Task<IReadOnlyList<IncidentMemoryHit>> SearchAsync(
        IncidentMemoryQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            var generated = await _embedder.GenerateAsync(
                new[] { query.QueryText }, null, cancellationToken);
            ReadOnlyMemory<float> queryVector = generated[0].Vector;

            var options = new VectorSearchOptions<IncidentMemoryDocument>();

            var hits = new List<IncidentMemoryHit>();
            await foreach (var r in _collection.SearchAsync(
                queryVector, query.MaxResults * 3, options, cancellationToken))
            {
                if (r.Record.TenantId != query.TenantId)
                    continue;
                double score = r.Score ?? 0d;
                if (score < query.MinScore)
                    continue;

                hits.Add(new IncidentMemoryHit(
                    RunId:            r.Record.RunId,
                    TenantId:         r.Record.TenantId,
                    AlertFingerprint: r.Record.AlertFingerprint,
                    SummarySnippet:   r.Record.SummaryText.Length <= 200
                                          ? r.Record.SummaryText
                                          : r.Record.SummaryText[..200],
                    Score:            score,
                    CreatedAtUtc:     r.Record.CreatedAtUtc));

                if (hits.Count >= query.MaxResults)
                    break;
            }

            return hits;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Incident memory search failed; returning empty results.");
            return Array.Empty<IncidentMemoryHit>();
        }
    }
}
