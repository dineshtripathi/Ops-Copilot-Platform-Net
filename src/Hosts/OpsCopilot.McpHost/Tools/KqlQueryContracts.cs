namespace OpsCopilot.McpHost.Tools;

/// <summary>
/// Request contract for POST /mcp/tools/kql_query.
/// Mirrors <c>OpsCopilot.AgentRuns.Application.Abstractions.KqlToolRequest</c>
/// but is intentionally kept in-process to preserve McpHost's autonomy.
/// </summary>
/// <param name="TenantId">Identifying tenant — forwarded for audit purposes.</param>
/// <param name="WorkspaceIdOrName">Log Analytics workspace resource ID or customer-defined name.</param>
/// <param name="Kql">KQL query string to execute.</param>
/// <param name="TimespanIso8601">
///   ISO 8601 duration string (e.g. "PT120M").
///   Parsed via <see cref="System.Xml.XmlConvert.ToTimeSpan"/>.
/// </param>
internal sealed record KqlQueryRequest(
    string TenantId,
    string WorkspaceIdOrName,
    string Kql,
    string TimespanIso8601);

/// <summary>
/// Response contract for POST /mcp/tools/kql_query.
/// On success <see cref="Ok"/> is true and <see cref="Rows"/> contains the result set.
/// On failure <see cref="Ok"/> is false and <see cref="Error"/> describes the problem.
/// </summary>
internal sealed record KqlQueryResponse(
    bool   Ok,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    string ExecutedQuery,
    string WorkspaceId,
    string Timespan,
    DateTime ExecutedAtUtc,
    string? Error,
    KqlQueryStats? Stats);

internal sealed record KqlQueryStats(long RowCount, long? IngestedDataBytes);
