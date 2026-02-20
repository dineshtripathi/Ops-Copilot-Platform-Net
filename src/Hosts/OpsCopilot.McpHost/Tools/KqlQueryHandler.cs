using System.Xml;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;

namespace OpsCopilot.McpHost.Tools;

/// <summary>
/// Executes KQL queries against Azure Log Analytics via the Azure Monitor
/// Query SDK.  This handler is the ONLY component in the solution that is
/// permitted to call <see cref="LogsQueryClient"/>.
///
/// The MCP hard-boundary rule: ApiHost must NOT reference Azure.Monitor.Query
/// or call Log Analytics directly — all queries travel through this handler
/// via HTTP POST /mcp/tools/kql_query.
/// </summary>
internal sealed class KqlQueryHandler
{
    private readonly LogsQueryClient _client;
    private readonly ILogger<KqlQueryHandler> _log;

    public KqlQueryHandler(LogsQueryClient client, ILogger<KqlQueryHandler> log)
    {
        _client = client;
        _log    = log;
    }

    public async Task<KqlQueryResponse> ExecuteAsync(
        KqlQueryRequest  request,
        CancellationToken ct)
    {
        TimeSpan timeSpan;
        try
        {
            // ISO 8601 duration → TimeSpan (e.g. "PT120M" → 2 hours)
            timeSpan = XmlConvert.ToTimeSpan(request.TimespanIso8601);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Invalid ISO 8601 duration: {Iso}", request.TimespanIso8601);
            return Failure(request, request.Kql, $"Invalid TimespanIso8601 value: {ex.Message}");
        }

        var executedAt = DateTime.UtcNow;

        _log.LogInformation(
            "Executing KQL for tenant {TenantId}, workspace {Workspace}, timespan {Timespan}",
            request.TenantId, request.WorkspaceIdOrName, request.TimespanIso8601);

        LogsQueryResult result;
        try
        {
            var response = await _client.QueryWorkspaceAsync(
                workspaceId:  request.WorkspaceIdOrName,
                query:        request.Kql,
                timeRange:    new QueryTimeRange(timeSpan),
                cancellationToken: ct);

            result = response.Value;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "KQL query failed for workspace {Workspace}", request.WorkspaceIdOrName);
            return Failure(request, request.Kql, ex.Message);
        }

        var table = result.Table;
        var rows  = BuildRows(table);

        var stats = new KqlQueryStats(
            RowCount:          rows.Count,
            IngestedDataBytes: null); // not exposed in this SDK version

        bool partialFailure = result.Status == LogsQueryResultStatus.PartialFailure;
        if (partialFailure)
            _log.LogWarning("KQL query returned PartialFailure; returning partial rows.");

        return new KqlQueryResponse(
            Ok:           !partialFailure,
            Rows:         rows,
            ExecutedQuery: request.Kql,
            WorkspaceId:  request.WorkspaceIdOrName,
            Timespan:     request.TimespanIso8601,
            ExecutedAtUtc: executedAt,
            Error:        partialFailure ? result.Error?.Message : null,
            Stats:        stats);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> BuildRows(
        LogsTable table)
    {
        var result = new List<Dictionary<string, object?>>(table.Rows.Count);
        var columns = table.Columns;

        foreach (var row in table.Rows)
        {
            var dict = new Dictionary<string, object?>(columns.Count, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                dict[col.Name] = row[col.Name] is { } v ? (object?)v.ToString() : null;
            }
            result.Add(dict);
        }

        return result;
    }

    private static KqlQueryResponse Failure(KqlQueryRequest req, string kql, string error)
        => new(
            Ok:           false,
            Rows:         Array.Empty<IReadOnlyDictionary<string, object?>>(),
            ExecutedQuery: kql,
            WorkspaceId:  req.WorkspaceIdOrName,
            Timespan:     req.TimespanIso8601,
            ExecutedAtUtc: DateTime.UtcNow,
            Error:        error,
            Stats:        null);
}
