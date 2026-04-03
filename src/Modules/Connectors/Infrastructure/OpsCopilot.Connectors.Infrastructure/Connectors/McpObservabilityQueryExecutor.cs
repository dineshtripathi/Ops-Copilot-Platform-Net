using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using OpsCopilot.Connectors.Abstractions;
using SdkMcpClient = ModelContextProtocol.Client.McpClient;
using StdioTransport = ModelContextProtocol.Client.StdioClientTransport;
using StdioTransportOptions = ModelContextProtocol.Client.StdioClientTransportOptions;
using HttpTransport = ModelContextProtocol.Client.HttpClientTransport;
using HttpTransportOptions = ModelContextProtocol.Client.HttpClientTransportOptions;

namespace OpsCopilot.Connectors.Infrastructure.Connectors;

// KQL-audit: safe — query text not logged
internal sealed class McpObservabilityQueryExecutor : IObservabilityQueryExecutor, IMcpToolConnector, IAsyncDisposable
{
    private const int DefaultTimespanMinutes = 43_200; // 30 days — matches ago(30d) KQL fallback
    private const int MaxRows = 200;
    private const int MaxPayloadChars = 20_000;

    private static readonly string[] BlockedPatterns =
    [
        ".create", ".alter", ".drop", ".ingest",
        ".set", ".append", ".delete", ".execute"
    ];

    private readonly McpObservabilityOptions _options;
    private readonly ILogger<McpObservabilityQueryExecutor> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SdkMcpClient? _mcpClient;
    private bool _disposed;

    public McpObservabilityQueryExecutor(
        McpObservabilityOptions options,
        ILogger<McpObservabilityQueryExecutor> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<QueryExecutionResult> ExecuteQueryAsync(
        string workspaceId,
        string queryText,
        TimeSpan? timespan,
        CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;

        if (string.IsNullOrWhiteSpace(queryText))
            return Failure("invalid_query", "Query text is null or empty.", startedAt);

        foreach (var pattern in BlockedPatterns)
        {
            if (queryText.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "[McpObservabilityQueryExecutor] Query blocked — contains '{Pattern}'", pattern);
                return Failure("blocked_query_pattern", $"Query contains blocked pattern '{pattern}'", startedAt);
            }
        }

        var minutes = timespan.HasValue
            ? Math.Clamp((int)timespan.Value.TotalMinutes, 1, 43_200)
            : DefaultTimespanMinutes;
        var timespanIso8601 = TimeSpan.FromMinutes(minutes).ToString("c") switch
        {
            var ts when ts.StartsWith("00:") => $"PT{minutes}M",
            _ => $"PT{minutes}M"
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            var client = await GetOrCreateClientAsync(cts.Token);

            _logger.LogInformation(
                "[McpObservabilityQueryExecutor] Calling MCP kql_query | workspace={WorkspaceId} | timespan={TimespanMinutes}m",
                workspaceId, minutes);

            var result = await client.CallToolAsync(
                "kql_query",
                new Dictionary<string, object?>
                {
                    ["workspaceId"] = workspaceId,
                    ["kql"] = queryText,
                    ["timespan"] = timespanIso8601,
                },
                cancellationToken: cts.Token);

            var textBlock = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            if (textBlock is null || string.IsNullOrWhiteSpace(textBlock.Text))
            {
                return Failure("empty_response", "MCP kql_query returned no text content.", startedAt);
            }

            return ParseMcpResponse(textBlock.Text, startedAt);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failure("mcp_timeout", $"MCP kql_query timed out after {_options.TimeoutSeconds}s.", startedAt);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[McpObservabilityQueryExecutor] MCP kql_query failed | workspace={WorkspaceId}", workspaceId);
            await InvalidateClientAsync();
            return Failure(ex.GetType().Name, ex.Message, startedAt);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_mcpClient is not null)
        {
            try
            {
                await _mcpClient.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[McpObservabilityQueryExecutor] Error disposing MCP client.");
            }

            _mcpClient = null;
        }

        _initLock.Dispose();
    }

    /// <inheritdoc />
    public async Task<string?> CallToolAsync(
        string toolName,
        Dictionary<string, object?> args,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            var client = await GetOrCreateClientAsync(cts.Token);

            _logger.LogInformation(
                "[McpObservabilityQueryExecutor] Calling MCP tool '{ToolName}'", toolName);

            var result    = await client.CallToolAsync(toolName, args, cancellationToken: cts.Token);
            var textBlock = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            return textBlock?.Text;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "[McpObservabilityQueryExecutor] Tool call '{ToolName}' timed out after {TimeoutSeconds}s",
                toolName, _options.TimeoutSeconds);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[McpObservabilityQueryExecutor] Tool call '{ToolName}' failed", toolName);
            await InvalidateClientAsync();
            return null;
        }
    }

    private async Task<SdkMcpClient> GetOrCreateClientAsync(CancellationToken ct)
    {
        if (_mcpClient is not null)
            return _mcpClient;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_mcpClient is not null)
                return _mcpClient;

            if (!string.IsNullOrWhiteSpace(_options.ServerUrl))
            {
                _logger.LogInformation(
                    "Connecting to McpHost via HTTP | url={Url}", _options.ServerUrl);
                var httpTransport = new HttpTransport(new HttpTransportOptions
                {
                    Endpoint = new Uri(_options.ServerUrl),
                });
                _mcpClient = await SdkMcpClient.CreateAsync(httpTransport, cancellationToken: ct);
            }
            else
            {
                var workDir = _options.WorkingDirectory ?? DiscoverSolutionRoot();
                var transport = new StdioTransport(new StdioTransportOptions
                {
                    Name = "OpsCopilotMcpHost-Observability",
                    Command = _options.Executable,
                    Arguments = _options.Arguments.ToList(),
                    WorkingDirectory = workDir,
                    EnvironmentVariables = BuildChildEnvironment()
                });
                _mcpClient = await SdkMcpClient.CreateAsync(transport, cancellationToken: ct);
            }
            return _mcpClient;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task InvalidateClientAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (_mcpClient is not null)
            {
                try
                {
                    await _mcpClient.DisposeAsync();
                }
                catch
                {
                }

                _mcpClient = null;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private QueryExecutionResult ParseMcpResponse(string json, DateTimeOffset startedAt)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();
            var errorText = TryString(root, "error");
            var rows = new List<Dictionary<string, object?>>();
            string[]? columns = null;

            if (root.TryGetProperty("tables", out var tablesEl) && tablesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var tableEl in tablesEl.EnumerateArray())
                {
                    if (!tableEl.TryGetProperty("columns", out var colsEl) ||
                        !tableEl.TryGetProperty("rows", out var rowArrayEl))
                    {
                        continue;
                    }

                    var tableColumns = colsEl.EnumerateArray()
                        .Select(c => c.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty)
                        .ToArray();

                    columns ??= tableColumns;

                    foreach (var rowEl in rowArrayEl.EnumerateArray())
                    {
                        var row = new Dictionary<string, object?>(tableColumns.Length, StringComparer.OrdinalIgnoreCase);
                        var index = 0;
                        foreach (var cellEl in rowEl.EnumerateArray())
                        {
                            var columnName = index < tableColumns.Length ? tableColumns[index] : $"col{index}";
                            row[columnName] = ExtractCellValue(cellEl);
                            index++;
                        }

                        rows.Add(row);
                    }
                }
            }

            var limitedRows = rows.Take(MaxRows).ToList();
            var resultJson = JsonSerializer.Serialize(limitedRows);
            if (resultJson.Length > MaxPayloadChars)
                resultJson = resultJson[..MaxPayloadChars];

            return new QueryExecutionResult(
                Success: ok,
                ResultJson: ok ? resultJson : null,
                RowCount: limitedRows.Count,
                ErrorMessage: ok ? null : errorText,
                Columns: columns,
                DurationMs: (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                ErrorCode: ok ? null : "mcp_query_failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[McpObservabilityQueryExecutor] Failed to parse MCP response.");
            return Failure("parse_error", $"Failed to parse MCP response: {ex.Message}", startedAt);
        }
    }

    private static string? TryString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var el) ? el.GetString() : null;

    private static object? ExtractCellValue(JsonElement cell)
        => cell.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => cell.TryGetInt64(out var l) ? l : cell.TryGetDouble(out var d) ? d : cell.GetRawText(),
            JsonValueKind.String => cell.GetString(),
            _ => cell.GetRawText(),
        };

    private static QueryExecutionResult Failure(string errorCode, string detail, DateTimeOffset startedAt)
        => new(
            Success: false,
            ResultJson: null,
            RowCount: 0,
            ErrorMessage: detail,
            Columns: null,
            DurationMs: (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
            ErrorCode: errorCode);

    private static Dictionary<string, string?> BuildChildEnvironment()
    {
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string[] wellKnown =
        [
            "PATH", "PATHEXT", "USERPROFILE", "HOME", "TEMP", "TMP",
            "SystemRoot", "SystemDrive", "COMSPEC", "windir",
            "APPDATA", "LOCALAPPDATA", "HOMEDRIVE", "HOMEPATH", "ProgramData",
            "ProgramFiles", "ProgramFiles(x86)",
            "DOTNET_ROOT", "DOTNET_CLI_HOME",
            "AZURE_CONFIG_DIR", "AZURE_TENANT_ID",
            "MSI_ENDPOINT", "MSI_SECRET",
            "IDENTITY_ENDPOINT", "IDENTITY_HEADER",
            "ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT",
            "WORKSPACE_ID",
        ];

        foreach (var name in wellKnown)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
                env[name] = value;
        }

        foreach (var entry in Environment.GetEnvironmentVariables())
        {
            if (entry is System.Collections.DictionaryEntry de
                && de.Key is string key
                && de.Value is string val
                && key.StartsWith("AzureAuth__", StringComparison.OrdinalIgnoreCase)
                && !env.ContainsKey(key))
            {
                env[key] = val;
            }
        }

        return env;
    }

    private static string? DiscoverSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }
}