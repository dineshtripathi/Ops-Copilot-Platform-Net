using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.Identity;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpsCopilot.Connectors.Abstractions;

namespace OpsCopilot.Connectors.Infrastructure.Connectors;

/// <summary>
/// Executes read-only KQL queries against Azure Monitor / Log Analytics.
/// <para>
/// Safety guardrails:
/// <list type="bullet">
///   <item>Read-only — all mutating KQL commands are blocked via substring check.</item>
///   <item>TimespanMinutes clamped to [1..1440].</item>
///   <item>Row cap 200, payload cap 20 KB.</item>
///   <item>Uses <c>DefaultAzureCredential</c> — no secrets in code/logs.</item>
/// </list>
/// </para>
/// </summary>
// KQL-audit: safe — query text not logged
internal sealed class AzureMonitorObservabilityQueryExecutor : IObservabilityQueryExecutor
{
    private const int DefaultTimespanMinutes = 60;
    private const int MaxRows = 200;
    private const int MaxPayloadChars = 20_000;

    /// <summary>
    /// Blocked KQL command prefixes (case-insensitive substring match).
    /// Any query containing these patterns is rejected before execution.
    /// </summary>
    private static readonly string[] BlockedPatterns =
    [
        ".create", ".alter", ".drop", ".ingest",
        ".set", ".append", ".delete", ".execute"
    ];

    private readonly LogsQueryClient _client;
    private readonly ILogger<AzureMonitorObservabilityQueryExecutor> _logger;
    private readonly int _timeoutMs;

    public AzureMonitorObservabilityQueryExecutor(
        LogsQueryClient client,
        IConfiguration configuration,
        ILogger<AzureMonitorObservabilityQueryExecutor> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeoutMs = int.TryParse(configuration["Packs:AzureMonitorQueryTimeoutMs"], out var t) ? t : 5000;
    }

    /// <inheritdoc />
    public async Task<QueryExecutionResult> ExecuteQueryAsync(
        string workspaceId,
        string queryText,
        TimeSpan? timespan,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // ── 1. Validate query text ──────────────────────────────────
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return Fail("invalid_query", "Query text is null or empty.", sw);
        }

        // ── 2. Blocked-pattern guardrail ────────────────────────────
        foreach (var pattern in BlockedPatterns)
        {
            if (queryText.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "[ObservabilityQueryExecutor] Query blocked — contains '{Pattern}'", pattern);
                return Fail("blocked_query_pattern",
                    $"Query contains blocked pattern '{pattern}'", sw);
            }
        }

        // ── 3. Resolve & clamp timespan ─────────────────────────────
        var minutes = timespan.HasValue
            ? Math.Clamp((int)timespan.Value.TotalMinutes, 1, 1440)
            : DefaultTimespanMinutes;
        var resolvedTimespan = TimeSpan.FromMinutes(minutes);

        // ── 4. Execute with timeout ─────────────────────────────────
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeoutMs);

        try
        {
            _logger.LogInformation(
                "[ObservabilityQueryExecutor] Querying workspace {WorkspaceId}, timespan={TimespanMinutes}m",
                workspaceId, minutes);

            var response = await _client.QueryWorkspaceAsync(
                workspaceId, queryText, new QueryTimeRange(resolvedTimespan),
                cancellationToken: cts.Token);

            var table = response.Value.Table;
            var columns = table.Columns;
            var rows = table.Rows;

            // Serialise rows as an array of objects keyed by column name
            var rowCount = Math.Min(rows.Count, MaxRows);
            var rowObjects = new List<Dictionary<string, object?>>(rowCount);
            for (var r = 0; r < rowCount; r++)
            {
                var row = rows[r];
                var obj = new Dictionary<string, object?>(columns.Count);
                for (var i = 0; i < columns.Count; i++)
                {
                    obj[columns[i].Name] = row[i];
                }
                rowObjects.Add(obj);
            }

            var resultJson = JsonSerializer.Serialize(rowObjects);
            var columnNames = columns.Select(c => c.Name).ToArray();

            // Truncate payload if needed
            if (resultJson.Length > MaxPayloadChars)
            {
                resultJson = resultJson[..MaxPayloadChars];
            }

            sw.Stop();

            _logger.LogInformation(
                "[ObservabilityQueryExecutor] Query succeeded — {RowCount} rows in {DurationMs}ms",
                rowCount, sw.ElapsedMilliseconds);

            return new QueryExecutionResult(
                Success: true,
                ResultJson: resultJson,
                RowCount: rowCount,
                ErrorMessage: null,
                Columns: columnNames,
                DurationMs: sw.ElapsedMilliseconds);
        }
        catch (AuthenticationFailedException ex)
        {
            _logger.LogError(ex, "[ObservabilityQueryExecutor] Authentication failed");
            return Fail("azure_auth_failed", ex.Message, sw);
        }
        catch (RequestFailedException ex) when (ex.Status == 403)
        {
            _logger.LogWarning(ex, "[ObservabilityQueryExecutor] Forbidden (403)");
            return Fail("azure_forbidden", ex.Message, sw);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning(ex, "[ObservabilityQueryExecutor] Workspace not found (404)");
            return Fail("azure_not_found", ex.Message, sw);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "[ObservabilityQueryExecutor] Request failed ({Status})", ex.Status);
            return Fail("azure_request_failed", ex.Message, sw);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "[ObservabilityQueryExecutor] Query timed out after {TimeoutMs}ms", _timeoutMs);
            return Fail("azure_monitor_timeout",
                $"Query timed out after {_timeoutMs}ms", sw);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ObservabilityQueryExecutor] Unexpected error");
            return Fail("unexpected_error", ex.Message, sw);
        }
    }

    private static QueryExecutionResult Fail(string errorCode, string detail, Stopwatch sw)
    {
        sw.Stop();
        return new QueryExecutionResult(
            Success: false,
            ResultJson: null,
            RowCount: 0,
            ErrorMessage: detail,
            ErrorCode: errorCode,
            DurationMs: sw.ElapsedMilliseconds);
    }
}
