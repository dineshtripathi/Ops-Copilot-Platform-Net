using Microsoft.Extensions.VectorData;

namespace OpsCopilot.Rag.Domain;

/// <summary>
/// A single incident stored in the vector memory store.
/// One document per completed triage run.
/// </summary>
public sealed class IncidentMemoryDocument
{
    [VectorStoreKey]
    public Guid Id { get; init; }

    [VectorStoreData]
    public string TenantId { get; init; } = string.Empty;

    [VectorStoreData]
    public string AlertFingerprint { get; init; } = string.Empty;

    [VectorStoreData]
    public string RunId { get; init; } = string.Empty;

    /// <summary>Short prose summary written at triage completion (≤500 chars).</summary>
    [VectorStoreData]
    public string SummaryText { get; init; } = string.Empty;

    [VectorStoreData]
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Embedding of <see cref="SummaryText"/> (1536-dim, text-embedding-3-small).</summary>
    [VectorStoreVector(Dimensions: 1536, DistanceFunction = DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float> Embedding { get; init; }
}
