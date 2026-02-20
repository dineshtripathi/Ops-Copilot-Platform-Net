namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>
/// Stable response schema returned by McpHost's /mcp/tools/kql_query endpoint.
/// Every field is populated regardless of success or failure, enabling citation
/// evidence to be stored even on degraded runs.
/// </summary>
public sealed record KqlToolResponse(
    bool   Ok,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    string ExecutedQuery,
    string WorkspaceId,
    string Timespan,
    DateTimeOffset ExecutedAtUtc,
    string? Error = null,
    object? Stats = null);
