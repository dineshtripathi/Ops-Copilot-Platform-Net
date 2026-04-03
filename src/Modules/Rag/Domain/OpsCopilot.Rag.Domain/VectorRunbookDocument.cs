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

    /// <summary>Tenant that owns this runbook. Used for ACL enforcement at retrieval time.</summary>
    [VectorStoreData]
    public string TenantId { get; init; } = string.Empty;

    [VectorStoreData]
    public string RunbookId { get; init; } = string.Empty;

    [VectorStoreData]
    public string Title { get; init; } = string.Empty;

    [VectorStoreData]
    public string Content { get; init; } = string.Empty;

    [VectorStoreData]
    public string Tags { get; init; } = string.Empty;

    /// <summary>Identifies the embedding model used (e.g. "text-embedding-3-small"). Required for version guard (PDD §2.2.10).</summary>
    [VectorStoreData]
    public string EmbeddingModelId { get; init; } = string.Empty;

    /// <summary>Monotonically incrementing version token. Retrieved documents whose version differs from the configured version are skipped.</summary>
    [VectorStoreData]
    public string EmbeddingVersion { get; init; } = string.Empty;

    /// <summary>Embedding of <see cref="Content"/> (1536-dim, text-embedding-3-small).</summary>
    [VectorStoreVector(Dimensions: 1536, DistanceFunction = DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float> Embedding { get; init; }
}
