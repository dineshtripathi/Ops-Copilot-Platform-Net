using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using OpsCopilot.Rag.Application;
using OpsCopilot.Rag.Domain;

namespace OpsCopilot.Rag.Infrastructure.Retrieval;

internal sealed class VectorRunbookRetrievalService : IRunbookRetrievalService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>>       _embedder;
    private readonly VectorStoreCollection<Guid, VectorRunbookDocument>  _collection;
    private readonly ILogger<VectorRunbookRetrievalService>              _logger;
    private readonly string                                               _embeddingVersion;

    public VectorRunbookRetrievalService(
        IEmbeddingGenerator<string, Embedding<float>>       embedder,
        VectorStoreCollection<Guid, VectorRunbookDocument>  collection,
        ILogger<VectorRunbookRetrievalService>              logger,
        string                                              embeddingVersion)
    {
        _embedder         = embedder;
        _collection       = collection;
        _logger           = logger;
        _embeddingVersion = embeddingVersion;
    }

    public async Task<IReadOnlyList<RunbookSearchResult>> SearchAsync(
        RunbookSearchQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            var generated = await _embedder.GenerateAsync(
                new[] { query.Query }, null, cancellationToken);
            ReadOnlyMemory<float> queryVector = generated[0].Vector;

            // Over-fetch when tenant filtering is active so we hit MaxResults after filtering.
            bool filterByTenant = !string.IsNullOrEmpty(query.TenantId);
            int fetchCount = filterByTenant ? query.MaxResults * 3 : query.MaxResults;

            var options = new VectorSearchOptions<VectorRunbookDocument>();

            var hits = new List<RunbookSearchResult>();
            await foreach (var r in _collection.SearchAsync(
                queryVector, fetchCount, options, cancellationToken))
            {
                // ACL enforcement — skip documents that belong to a different tenant.
                if (filterByTenant && r.Record.TenantId != query.TenantId)
                    continue;

                // Embedding version guard (PDD §2.2.10) — skip stale documents from a different embedding version.
                if (!string.IsNullOrEmpty(_embeddingVersion) && r.Record.EmbeddingVersion != _embeddingVersion)
                {
                    _logger.LogWarning(
                        "Skipping runbook '{RunbookId}' with embedding version '{DocVersion}'; expected '{Expected}'.",
                        r.Record.RunbookId, r.Record.EmbeddingVersion, _embeddingVersion);
                    continue;
                }

                double score = r.Score ?? 0d;
                var snippet = r.Record.Content.Length <= 300
                    ? r.Record.Content
                    : r.Record.Content[..300];

                hits.Add(new RunbookSearchResult(
                    RunbookId: r.Record.RunbookId,
                    Title:     r.Record.Title,
                    Snippet:   snippet,
                    Score:     score));

                if (hits.Count >= query.MaxResults)
                    break;
            }

            return hits;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector runbook search failed; returning empty results.");
            return Array.Empty<RunbookSearchResult>();
        }
    }
}
