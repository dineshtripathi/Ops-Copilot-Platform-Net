namespace OpsCopilot.Rag.Application;

/// <summary>
/// Input for a runbook retrieval search.
/// </summary>
/// <param name="Query">Free-text search query.</param>
/// <param name="MaxResults">Maximum number of results to return (default 5).</param>
/// <param name="TenantId">
/// Caller's tenant identifier used for ACL enforcement.
/// When non-empty, vector retrieval services filter results to this tenant only.
/// Empty string bypasses tenant filtering (development / in-memory mode only).
/// </param>
public sealed record RunbookSearchQuery(string Query, int MaxResults = 5, string TenantId = "");
