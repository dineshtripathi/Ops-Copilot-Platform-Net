namespace OpsCopilot.Rag.Application;

/// <summary>
/// Input for a runbook retrieval search.
/// </summary>
/// <param name="Query">Free-text search query.</param>
/// <param name="MaxResults">Maximum number of results to return (default 5).</param>
public sealed record RunbookSearchQuery(string Query, int MaxResults = 5);
