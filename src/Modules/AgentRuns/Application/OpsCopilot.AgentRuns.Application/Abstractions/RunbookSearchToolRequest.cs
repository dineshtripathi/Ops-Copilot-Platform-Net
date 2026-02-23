namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>
/// Stable request schema for the MCP runbook_search tool.
/// ApiHost sends this; McpHost searches the runbook knowledge base.
/// </summary>
/// <param name="Query">Keywords to search for in the runbook knowledge base.</param>
/// <param name="MaxResults">Maximum number of results to return (1–20, default 5).</param>
public sealed record RunbookSearchToolRequest(
    string Query,
    int MaxResults = 5);
