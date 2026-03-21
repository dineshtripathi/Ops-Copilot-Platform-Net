namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Slice 90: projection of a prior incident retrieved from vector memory.
/// Read-only summary — no raw embeddings, no payloads, no PII beyond the stored snippet.
/// </summary>
public sealed record SimilarPriorIncident(
    Guid           PriorRunId,
    string?        AlertFingerprint,
    string         SummarySnippet,
    double         Score,
    DateTimeOffset OccurredAtUtc);
