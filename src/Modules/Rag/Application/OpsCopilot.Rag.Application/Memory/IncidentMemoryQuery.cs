namespace OpsCopilot.Rag.Application.Memory;

/// <summary>
/// Query parameters for incident memory retrieval.
/// </summary>
/// <param name="QueryText">Text to embed and search against.</param>
/// <param name="TenantId">Tenant isolation — only hits for this tenant are returned.</param>
/// <param name="MaxResults">Max hits to return after tenant filtering.</param>
/// <param name="MinScore">Minimum cosine similarity score threshold (0–1).</param>
public sealed record IncidentMemoryQuery(
    string QueryText,
    string TenantId,
    int    MaxResults = 5,
    double MinScore   = 0.7);
