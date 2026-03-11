using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using OpsCopilot.Rag.Application.Memory;
using OpsCopilot.Rag.Domain;

namespace OpsCopilot.Rag.Infrastructure.Memory;

internal sealed class VectorIncidentMemoryIndexer : IIncidentMemoryIndexer
{
    private readonly IEmbeddingGenerator<string, Embedding<float>>          _embedder;
    private readonly VectorStoreCollection<Guid, IncidentMemoryDocument>   _collection;

    public VectorIncidentMemoryIndexer(
        IEmbeddingGenerator<string, Embedding<float>>            embedder,
        VectorStoreCollection<Guid, IncidentMemoryDocument>      collection)
    {
        _embedder   = embedder;
        _collection = collection;
    }

    public async Task IndexAsync(
        IncidentMemoryDocument document, CancellationToken cancellationToken = default)
    {
        var generated = await _embedder.GenerateAsync(
            new[] { document.SummaryText }, null, cancellationToken);
        ReadOnlyMemory<float> vector = generated[0].Vector;

        var withEmbedding = new IncidentMemoryDocument
        {
            Id               = document.Id,
            TenantId         = document.TenantId,
            AlertFingerprint = document.AlertFingerprint,
            RunId            = document.RunId,
            SummaryText      = document.SummaryText,
            CreatedAtUtc     = document.CreatedAtUtc,
            Embedding        = vector,
        };
        await _collection.UpsertAsync(withEmbedding, cancellationToken);
    }
}
