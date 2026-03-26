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

    public VectorRunbookRetrievalService(
        IEmbeddingGenerator<string, Embedding<float>>       embedder,
        VectorStoreCollection<Guid, VectorRunbookDocument>  collection,
        ILogger<VectorRunbookRetrievalService>              logger)
    {
        _embedder   = embedder;
        _collection = collection;
        _logger     = logger;
    }

    public async Task<IReadOnlyList<RunbookSearchResult>> SearchAsync(
        RunbookSearchQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            var generated = await _embedder.GenerateAsync(
                new[] { query.Query }, null, cancellationToken);
            ReadOnlyMemory<float> queryVector = generated[0].Vector;

            var options = new VectorSearchOptions<VectorRunbookDocument>();

            var hits = new List<RunbookSearchResult>();
            await foreach (var r in _collection.SearchAsync(
                queryVector, query.MaxResults, options, cancellationToken))
            {
                double score = r.Score ?? 0d;
                var snippet = r.Record.Content.Length <= 300
                    ? r.Record.Content
                    : r.Record.Content[..300];

                hits.Add(new RunbookSearchResult(
                    RunbookId: r.Record.RunbookId,
                    Title:     r.Record.Title,
                    Snippet:   snippet,
                    Score:     score));
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
