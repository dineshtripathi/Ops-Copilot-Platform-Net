using System.ComponentModel;
using System.Text.Json;
using System.Xml;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace OpsCopilot.McpHost.Tools;

/// <summary>
/// Exposes the "kql_query" MCP tool.
///
/// The class is discovered automatically by <c>WithToolsFromAssembly()</c>
/// via the <see cref="McpServerToolTypeAttribute"/> marker.
///
/// DI-injectable parameters (LogsQueryClient, ILogger) are resolved from the
/// host's service container per-invocation — no manual wiring required.
/// </summary>
[McpServerToolType]
public sealed class KqlQueryTool
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>
    /// Execute a KQL query against an Azure Log Analytics workspace.
    /// Returns result rows plus evidence metadata suitable for citations.
    ///
    /// On success  → ok=true,  tables populated, error=null.
    /// On failure  → ok=false, tables=[], error contains message+type.
    /// Partial     → ok=false, tables contain partial data, error explains.
    /// </summary>
    [McpServerTool(Name = "kql_query")]
    [Description(
        "Execute a KQL (Kusto Query Language) query against an Azure Log Analytics workspace. " +
        "Returns the result rows and evidence metadata (workspaceId, executedQuery, timespan, " +
        "executedAtUtc) for use as citations. " +
        "On failure returns ok=false with an error field describing what went wrong.")]
    public static async Task<string> ExecuteAsync(
        // Injected from DI — registered as singleton in Program.cs
        LogsQueryClient  logsClient,
        ILoggerFactory   loggerFactory,

        // MCP tool parameters — appear in the JSON input schema
        [Description(
            "Log Analytics workspace GUID in the form " +
            "'xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx'. " +
            "Found in Azure Portal → Log Analytics workspace → Overview.")]
        string workspaceId,

        [Description(
            "Non-empty KQL query to execute, e.g. " +
            "\"union traces, exceptions | where timestamp > ago(2h) | take 20\".")]
        string kql,

        [Description(
            "ISO 8601 duration for the query time range, e.g. 'PT2H' (2 hours), " +
            "'P1D' (1 day), 'PT30M' (30 minutes). Must be parseable by " +
            "System.Xml.XmlConvert.ToTimeSpan.")]
        string timespan,

        CancellationToken cancellationToken)
    {
        var executedAtUtc = DateTimeOffset.UtcNow;
        var logger        = loggerFactory.CreateLogger(nameof(KqlQueryTool));

        // ── Input validation ─────────────────────────────────────────────────
        if (!Guid.TryParse(workspaceId, out _))
        {
            return Fail(workspaceId, kql, timespan, executedAtUtc,
                $"workspaceId '{workspaceId}' is not a valid GUID.", "ValidationError");
        }

        if (string.IsNullOrWhiteSpace(kql))
        {
            return Fail(workspaceId, kql, timespan, executedAtUtc,
                "kql must not be empty or whitespace.", "ValidationError");
        }

        TimeSpan duration;
        try
        {
            // ISO 8601 duration → TimeSpan (e.g. "PT2H" → 2 hours)
            duration = XmlConvert.ToTimeSpan(timespan);
        }
        catch (Exception ex)
        {
            return Fail(workspaceId, kql, timespan, executedAtUtc,
                $"timespan '{timespan}' is not a valid ISO 8601 duration: {ex.Message}",
                "ValidationError");
        }

        // ── Query execution ──────────────────────────────────────────────────
        logger.LogInformation(
            "Executing KQL | workspace={WorkspaceId} | timespan={Timespan} | query={Kql}",
            workspaceId, timespan, kql);

        try
        {
            var response = await logsClient.QueryWorkspaceAsync(
                workspaceId,
                kql,
                new QueryTimeRange(duration),
                cancellationToken: cancellationToken);

            var result = response.Value;

            bool isPartial = result.Status == LogsQueryResultStatus.PartialFailure;
            bool isFailure = result.Status == LogsQueryResultStatus.Failure;

            if (isFailure)
            {
                var msg = result.Error?.Message ?? "Query returned Failure status with no detail.";
                logger.LogWarning("KQL Failure | workspace={WorkspaceId} | error={Error}", workspaceId, msg);
                return Fail(workspaceId, kql, timespan, executedAtUtc, msg, "QueryFailure");
            }

            var tables = result.AllTables
                .Select(t => SerializeTable(t))
                .ToArray();

            int totalRows = tables.Sum(t =>
            {
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(t, JsonOpts));
                return doc.RootElement.GetProperty("rows").GetArrayLength();
            });

            logger.LogInformation(
                "KQL completed | workspace={WorkspaceId} | status={Status} | tables={Tables} | rows={Rows}",
                workspaceId, result.Status, tables.Length, totalRows);

            var payload = new
            {
                ok            = !isPartial,
                workspaceId   = workspaceId,
                executedQuery = kql,
                timespan      = timespan,
                executedAtUtc = executedAtUtc,
                status        = result.Status.ToString(),
                tables        = tables,
                error         = isPartial
                    ? (result.Error?.Message ?? "Query returned PartialFailure — some data may be missing.")
                    : (string?)null,
            };

            return JsonSerializer.Serialize(payload, JsonOpts);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("KQL cancelled | workspace={WorkspaceId}", workspaceId);
            return Fail(workspaceId, kql, timespan, executedAtUtc,
                "Query was cancelled.", "OperationCancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "KQL execution failed | workspace={WorkspaceId}", workspaceId);
            return Fail(workspaceId, kql, timespan, executedAtUtc, ex.Message, ex.GetType().Name);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Serialises a <see cref="LogsTable"/> to a plain object ready for JSON output.
    /// Columns retain their declared type names for downstream type-awareness.
    /// Row values are mapped to their native CLR types (numbers, booleans, strings)
    /// so the LLM receives structured data rather than strings for everything.
    /// </summary>
    private static object SerializeTable(LogsTable table)
    {
        var columns = table.Columns
            .Select(c => new { name = c.Name, type = c.Type.ToString() })
            .ToArray();

        var rows = table.Rows
            .Select(row => table.Columns
                .Select(col => CellValue(row, col))
                .ToArray())
            .ToArray();

        return new { name = table.Name, columns, rows };
    }

    /// <summary>
    /// Extracts the typed value of a single cell, falling back to string
    /// representation for unsupported or unknown column types.
    /// All exceptions are swallowed — the cell becomes null rather than
    /// crashing an entire query result.
    /// </summary>
    private static object? CellValue(LogsTableRow row, LogsTableColumn col)
    {
        try
        {
            return col.Type.ToString() switch
            {
                "bool"     => (object?)row.GetBoolean(col.Name),
                "datetime" => row.GetDateTimeOffset(col.Name)?.ToString("O"),
                "guid"     => row.GetGuid(col.Name)?.ToString(),
                "int"      => row.GetInt32(col.Name),
                "long"     => row.GetInt64(col.Name),
                "real"     => row.GetDouble(col.Name),
                "decimal"  => row.GetDecimal(col.Name),
                "timespan" => row.GetTimeSpan(col.Name)?.ToString(),
                // "string", "dynamic", and anything unrecognised → string
                _          => row.GetString(col.Name),
            };
        }
        catch
        {
            // Never let a single bad cell kill the whole result.
            return null;
        }
    }

    /// <summary>Builds the standard failure JSON payload.</summary>
    private static string Fail(
        string        workspaceId,
        string        kql,
        string        timespan,
        DateTimeOffset executedAtUtc,
        string        message,
        string        errorType)
    {
        return JsonSerializer.Serialize(
            new
            {
                ok            = false,
                workspaceId   = workspaceId,
                executedQuery = kql,
                timespan      = timespan,
                executedAtUtc = executedAtUtc,
                status        = "Failed",
                tables        = Array.Empty<object>(),
                error         = $"[{errorType}] {message}",
            },
            JsonOpts);
    }
}
