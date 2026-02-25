using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpsCopilot.SafeActions.Application.Abstractions;

namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// Executes read-only Azure Monitor / Log Analytics KQL queries.
/// <para>
/// Safety guardrails:
/// <list type="bullet">
///   <item>Read-only — all mutating KQL commands are blocked via substring check.</item>
///   <item>WorkspaceId must be a valid GUID.</item>
///   <item>Workspace ID allowlist (<c>SafeActions:AllowedLogAnalyticsWorkspaceIds</c>) — empty = allow all.</item>
///   <item>TimespanMinutes clamped to [1..1440].</item>
///   <item>Uses <c>DefaultAzureCredential</c> — no secrets in code/logs.</item>
/// </list>
/// </para>
/// </summary>
internal sealed class AzureMonitorQueryActionExecutor
{
    private static readonly JsonDocumentOptions JsonOpts = new() { AllowTrailingCommas = true };

    /// <summary>
    /// Blocked KQL command prefixes (case-insensitive substring match).
    /// Any query containing these patterns is rejected before execution.
    /// </summary>
    private static readonly string[] BlockedPatterns =
    [
        ".create", ".alter", ".drop", ".ingest",
        ".set", ".append", ".delete", ".execute"
    ];

    private readonly IAzureMonitorLogsReader _reader;
    private readonly ILogger<AzureMonitorQueryActionExecutor> _logger;
    private readonly int _timeoutMs;
    private readonly HashSet<string> _allowedWorkspaceIds;

    public AzureMonitorQueryActionExecutor(
        IAzureMonitorLogsReader reader,
        IConfiguration configuration,
        ILogger<AzureMonitorQueryActionExecutor> logger)
    {
        _reader = reader;
        _logger = logger;
        _timeoutMs = configuration.GetValue("SafeActions:AzureMonitorQueryTimeoutMs", 5000);

        var raw = configuration.GetSection("SafeActions:AllowedLogAnalyticsWorkspaceIds")
            .Get<string[]>() ?? [];
        _allowedWorkspaceIds = new HashSet<string>(
            raw.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ActionExecutionResult> ExecuteAsync(string payloadJson, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // ── 1. Parse JSON ───────────────────────────────────────────
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(payloadJson, JsonOpts);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[AzureMonitorQueryExecutor] Invalid JSON payload");
            return Fail("invalid_json", ex.Message, sw);
        }

        var root = doc.RootElement;

        // ── 2. Extract & validate workspaceId ───────────────────────
        if (!root.TryGetProperty("workspaceId", out var wsElem) ||
            wsElem.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(wsElem.GetString()))
        {
            return Fail("invalid_payload", "Missing or empty 'workspaceId'", sw);
        }
        var workspaceId = wsElem.GetString()!;

        if (!Guid.TryParse(workspaceId, out _))
        {
            return Fail("invalid_workspace_id",
                $"WorkspaceId '{workspaceId}' is not a valid GUID", sw);
        }

        // ── Workspace allowlist ─────────────────────────────────────
        if (_allowedWorkspaceIds.Count > 0 &&
            !_allowedWorkspaceIds.Contains(workspaceId))
        {
            return Fail("target_not_allowlisted",
                $"WorkspaceId '{workspaceId}' is not in the allowed list", sw);
        }

        // ── 3. Extract & validate query ─────────────────────────────
        if (!root.TryGetProperty("query", out var queryElem) ||
            queryElem.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(queryElem.GetString()))
        {
            return Fail("invalid_payload", "Missing or empty 'query'", sw);
        }
        var query = queryElem.GetString()!;

        // ── 4. Blocked-pattern guardrail ────────────────────────────
        foreach (var pattern in BlockedPatterns)
        {
            if (query.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "[AzureMonitorQueryExecutor] Query blocked — contains '{Pattern}'", pattern);
                return Fail("blocked_query_pattern",
                    $"Query contains blocked pattern '{pattern}'", sw);
            }
        }

        // ── 5. Extract & clamp timespanMinutes ──────────────────────
        var timespanMinutes = 60; // default
        if (root.TryGetProperty("timespanMinutes", out var tsElem))
        {
            if (tsElem.ValueKind == JsonValueKind.Number && tsElem.TryGetInt32(out var parsed))
            {
                timespanMinutes = Math.Clamp(parsed, 1, 1440);
            }
        }

        // ── 6. Execute query with timeout ───────────────────────────
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeoutMs);

        try
        {
            _logger.LogInformation(
                "[AzureMonitorQueryExecutor] Querying workspace {WorkspaceId}, timespan={TimespanMinutes}m",
                workspaceId, timespanMinutes);

            var result = await _reader.QueryLogsAsync(
                workspaceId, query, TimeSpan.FromMinutes(timespanMinutes), cts.Token);

            sw.Stop();

            var response = JsonSerializer.Serialize(new
            {
                mode = "azure_monitor_query",
                success = true,
                workspaceId,
                rowCount = result.RowCount,
                columnCount = result.ColumnCount,
                rows = JsonSerializer.Deserialize<JsonElement>(result.ResultJson)
            });

            _logger.LogInformation(
                "[AzureMonitorQueryExecutor] Query succeeded — {RowCount} rows in {DurationMs}ms",
                result.RowCount, sw.ElapsedMilliseconds);

            return new ActionExecutionResult(true, response, sw.ElapsedMilliseconds);
        }
        catch (AuthenticationFailedException ex)
        {
            _logger.LogError(ex, "[AzureMonitorQueryExecutor] Authentication failed");
            return Fail("azure_auth_failed", ex.Message, sw);
        }
        catch (RequestFailedException ex) when (ex.Status == 403)
        {
            _logger.LogWarning(ex, "[AzureMonitorQueryExecutor] Forbidden (403)");
            return Fail("azure_forbidden", ex.Message, sw);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning(ex, "[AzureMonitorQueryExecutor] Workspace not found (404)");
            return Fail("azure_not_found", ex.Message, sw);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "[AzureMonitorQueryExecutor] Request failed ({Status})", ex.Status);
            return Fail("azure_request_failed", ex.Message, sw);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _logger.LogWarning("[AzureMonitorQueryExecutor] Query timed out after {TimeoutMs}ms", _timeoutMs);
            return Fail("azure_monitor_timeout",
                $"Query timed out after {_timeoutMs}ms", sw);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AzureMonitorQueryExecutor] Unexpected error");
            return Fail("unexpected_error", ex.Message, sw);
        }
    }

    public Task<ActionExecutionResult> RollbackAsync(string rollbackPayloadJson, CancellationToken ct)
    {
        var response = JsonSerializer.Serialize(new
        {
            mode = "azure_monitor_query",
            reason = "not_supported"
        });
        return Task.FromResult(new ActionExecutionResult(false, response, 0));
    }

    // ── helpers ─────────────────────────────────────────────────────

    private static ActionExecutionResult Fail(string reason, string detail, Stopwatch sw)
    {
        sw.Stop();
        var json = JsonSerializer.Serialize(new
        {
            mode = "azure_monitor_query",
            reason,
            detail
        });
        return new ActionExecutionResult(false, json, sw.ElapsedMilliseconds);
    }
}
