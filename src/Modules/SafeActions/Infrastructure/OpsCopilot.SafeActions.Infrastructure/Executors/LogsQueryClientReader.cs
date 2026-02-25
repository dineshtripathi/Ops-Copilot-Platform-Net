using System.Text.Json;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Logging;

namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// Production implementation of <see cref="IAzureMonitorLogsReader"/>.
/// Wraps <see cref="LogsQueryClient"/> from Azure.Monitor.Query SDK.
/// Read-only — no mutations are possible through this client.
/// </summary>
internal sealed class LogsQueryClientReader : IAzureMonitorLogsReader
{
    private readonly LogsQueryClient _client;
    private readonly ILogger<LogsQueryClientReader> _logger;

    public LogsQueryClientReader(LogsQueryClient client, ILogger<LogsQueryClientReader> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<MonitorQueryResult> QueryLogsAsync(
        string workspaceId, string query, TimeSpan timespan, CancellationToken ct)
    {
        _logger.LogInformation(
            "[LogsQueryClientReader] Querying workspace {WorkspaceId} with timespan {Timespan}",
            workspaceId, timespan);

        var response = await _client.QueryWorkspaceAsync(
            workspaceId, query, new QueryTimeRange(timespan), cancellationToken: ct);

        var table = response.Value.Table;
        var columns = table.Columns;
        var rows = table.Rows;

        // Serialise rows as an array of objects keyed by column name
        var rowObjects = new List<Dictionary<string, object?>>(rows.Count);
        foreach (var row in rows)
        {
            var obj = new Dictionary<string, object?>(columns.Count);
            for (var i = 0; i < columns.Count; i++)
            {
                obj[columns[i].Name] = row[i];
            }
            rowObjects.Add(obj);
        }

        var resultJson = JsonSerializer.Serialize(rowObjects);

        _logger.LogInformation(
            "[LogsQueryClientReader] Query returned {RowCount} rows, {ColumnCount} columns",
            rows.Count, columns.Count);

        return new MonitorQueryResult(rows.Count, columns.Count, resultJson);
    }
}
