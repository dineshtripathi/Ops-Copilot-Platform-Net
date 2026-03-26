using Microsoft.Extensions.VectorData;

namespace OpsCopilot.Rag.Domain;

/// <summary>
/// A runbook document stored in the vector store.
/// One document per indexed runbook markdown file.
/// </summary>
public sealed class VectorRunbookDocument
{
    [VectorStoreKey]
    public Guid Id { get; init; }

    [VectorStoreData]
    public string RunbookId { get; init; } = string.Empty;

    [VectorStoreData]
    public string Title { get; init; } = string.Empty;

    [VectorStoreData]
    public string Content { get; init; } = string.Empty;

    [VectorStoreData]
    public string Tags { get; init; } = string.Empty;

    /// <summary>Embedding of <see cref="Content"/> (1536-dim, text-embedding-3-small).</summary>
    [VectorStoreVector(Dimensions: 1536, DistanceFunction = DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float> Embedding { get; init; }
}
