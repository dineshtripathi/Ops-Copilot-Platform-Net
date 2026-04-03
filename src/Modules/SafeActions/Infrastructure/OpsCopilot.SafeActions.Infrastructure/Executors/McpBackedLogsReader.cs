using System.Text.Json;
using System.Xml;
using Microsoft.Extensions.Logging;
using OpsCopilot.SafeActions.Infrastructure.McpClient;

namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// MCP-backed implementation of <see cref="IAzureMonitorLogsReader"/>.
///
/// Routes all Log Analytics calls through the OpsCopilot.McpHost child process
/// via the <c>kql_query</c> MCP tool (same tool used by MetricsConnector).
///
/// Boundary rule: this class MUST NOT reference Azure.Monitor.Query.
/// </summary>
internal sealed class McpBackedLogsReader : IAzureMonitorLogsReader
{
    private readonly SafeActionsMcpHostClient       _mcp;
    private readonly ILogger<McpBackedLogsReader>   _logger;

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    public McpBackedLogsReader(
        SafeActionsMcpHostClient        mcp,
        ILogger<McpBackedLogsReader>    logger)
    {
        _mcp    = mcp;
        _logger = logger;
    }

    public async Task<MonitorQueryResult> QueryLogsAsync(
        string            workspaceId,
        string            query,
        TimeSpan          timespan,
        CancellationToken ct)
    {
        // ISO 8601 duration expected by the kql_query tool (e.g. "PT2H")
        var timespanStr = XmlConvert.ToString(timespan);

        var json = await _mcp.CallToolAsync(
            "kql_query",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["kql"]         = query,
                ["timespan"]    = timespanStr,
            },
            ct);

        var doc = JsonDocument.Parse(json).RootElement;

        if (!doc.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
        {
            var err = doc.TryGetProperty("error", out var ep)
                ? (ep.ValueKind == JsonValueKind.String ? ep.GetString() ?? json : ep.GetRawText())
                : json;
            _logger.LogWarning(
                "kql_query returned ok=false for workspace. error={Error}", err);
            // Return empty result with the error captured in 0-row / 0-column form
            return new MonitorQueryResult(RowCount: 0, ColumnCount: 0, ResultJson: "[]");
        }

        // Sum rows across all tables; column count from the first table
        var rowCount    = 0;
        var columnCount = 0;

        if (doc.TryGetProperty("tables", out var tables))
        {
            foreach (var table in tables.EnumerateArray())
            {
                if (table.TryGetProperty("rows", out var rows))
                    rowCount += rows.GetArrayLength();

                if (columnCount == 0 && table.TryGetProperty("columns", out var cols))
                    columnCount = cols.GetArrayLength();
            }
        }

        return new MonitorQueryResult(
            RowCount:    rowCount,
            ColumnCount: columnCount,
            ResultJson:  json);
    }
}
