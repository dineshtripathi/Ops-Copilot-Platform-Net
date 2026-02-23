namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>
/// Stable response schema returned by McpHost's runbook_search MCP tool.
/// Every field is populated regardless of success or failure, enabling citation
/// evidence to be stored even on degraded runs.
/// </summary>
public sealed record RunbookSearchToolResponse(
    bool Ok,
    IReadOnlyList<RunbookSearchHit> Hits,
    string Query,
    string? Error = null);

/// <summary>
/// A single search result from the runbook knowledge base.
/// </summary>
public sealed record RunbookSearchHit(
    string RunbookId,
    string Title,
    string Snippet,
    double Score);
