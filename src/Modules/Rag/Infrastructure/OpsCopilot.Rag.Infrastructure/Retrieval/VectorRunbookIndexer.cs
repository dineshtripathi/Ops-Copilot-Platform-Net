using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using OpsCopilot.Rag.Application;
using OpsCopilot.Rag.Domain;

namespace OpsCopilot.Rag.Infrastructure.Retrieval;

internal sealed class VectorRunbookIndexer : IRunbookIndexer
{
    private readonly IEmbeddingGenerator<string, Embedding<float>>      _embedder;
    private readonly VectorStoreCollection<Guid, VectorRunbookDocument> _collection;
    private readonly string                                              _embeddingModelId;
    private readonly string                                              _embeddingVersion;

    public VectorRunbookIndexer(
        IEmbeddingGenerator<string, Embedding<float>>      embedder,
        VectorStoreCollection<Guid, VectorRunbookDocument> collection,
        string                                             embeddingModelId,
        string                                             embeddingVersion)
    {
        _embedder         = embedder;
        _collection       = collection;
        _embeddingModelId = embeddingModelId;
        _embeddingVersion = embeddingVersion;
    }

    public async Task IndexAsync(
        VectorRunbookDocument document,
        CancellationToken     cancellationToken = default)
    {
        var textToEmbed = BuildEmbedText(document);
        var generated   = await _embedder.GenerateAsync(
            new[] { textToEmbed }, null, cancellationToken);
        ReadOnlyMemory<float> vector = generated[0].Vector;

        var withEmbedding = new VectorRunbookDocument
        {
            Id               = document.Id,
            TenantId         = document.TenantId,
            RunbookId        = document.RunbookId,
            Title            = document.Title,
            Content          = document.Content,
            Tags             = document.Tags,
            EmbeddingModelId = _embeddingModelId,
            EmbeddingVersion = _embeddingVersion,
            Embedding        = vector,
        };
        await _collection.UpsertAsync(withEmbedding, cancellationToken);
    }

    public async Task IndexBatchAsync(
        IEnumerable<VectorRunbookDocument> documents,
        CancellationToken                  cancellationToken = default)
    {
        foreach (var doc in documents)
            await IndexAsync(doc, cancellationToken);
    }

    // Combine title + content so the embedding captures both topical signal and body text.
    private static string BuildEmbedText(VectorRunbookDocument doc)
        => string.IsNullOrWhiteSpace(doc.Title)
            ? doc.Content
            : $"{doc.Title}\n{doc.Content}";
}
