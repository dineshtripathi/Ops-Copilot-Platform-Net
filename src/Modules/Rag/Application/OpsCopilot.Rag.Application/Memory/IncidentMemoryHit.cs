namespace OpsCopilot.Rag.Application.Memory;

/// <summary>A single result returned from incident memory retrieval.</summary>
/// <param name="RunId">The agent run that produced this memory document.</param>
/// <param name="TenantId">Tenant that owns this incident.</param>
/// <param name="AlertFingerprint">Alert fingerprint for deduplication.</param>
/// <param name="SummarySnippet">Short summary text (≤500 chars).</param>
/// <param name="Score">Cosine similarity score (0–1).</param>
/// <param name="CreatedAtUtc">When the memory document was indexed.</param>
public sealed record IncidentMemoryHit(
    string         RunId,
    string         TenantId,
    string         AlertFingerprint,
    string         SummarySnippet,
    double         Score,
    DateTimeOffset CreatedAtUtc);
