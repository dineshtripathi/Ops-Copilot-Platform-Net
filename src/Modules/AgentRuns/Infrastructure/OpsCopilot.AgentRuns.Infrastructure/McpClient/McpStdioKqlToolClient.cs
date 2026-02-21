using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using OpsCopilot.AgentRuns.Application.Abstractions;
// Alias needed: the containing namespace is also named McpClient, which
// would otherwise shadow the ModelContextProtocol.Client.McpClient type.
using SdkMcpClient = ModelContextProtocol.Client.McpClient;
using StdioTransport = ModelContextProtocol.Client.StdioClientTransport;
using StdioTransportOptions = ModelContextProtocol.Client.StdioClientTransportOptions;

namespace OpsCopilot.AgentRuns.Infrastructure.McpClient;

/// <summary>
/// Implements <see cref="IKqlToolClient"/> by launching the OpsCopilot.McpHost
/// process as a child process and communicating over stdio using the official
/// ModelContextProtocol C# SDK (StdioClientTransport, 0.9.0-preview.1).
///
/// Lifecycle:
///   - Registered as a singleton; the McpHost child process is created lazily
///     on the first <see cref="ExecuteAsync"/> call and reused for the lifetime
///     of the application.
///   - If the child process dies mid-flight, the failed call is returned as
///     ok=false with an error message; the internal client is invalidated so
///     the next call starts a fresh process.
///   - On application shutdown, <see cref="DisposeAsync"/> closes the child
///     process gracefully.
///
/// Boundary constraint:
///   This class MUST NOT reference Azure.Monitor.Query.
///   All KQL execution happens inside McpHost.
/// </summary>
public sealed class McpStdioKqlToolClient : IKqlToolClient, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private readonly McpKqlServerOptions         _options;
    private readonly ILogger<McpStdioKqlToolClient> _logger;

    // Lazy MCP client — initialised once on first call, guarded by _initLock.
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SdkMcpClient? _mcpClient;
    private bool          _disposed;

    public McpStdioKqlToolClient(
        McpKqlServerOptions             options,
        ILogger<McpStdioKqlToolClient>  logger)
    {
        _options = options;
        _logger  = logger;
    }

    // ── IKqlToolClient ────────────────────────────────────────────────────────

    /// <summary>
    /// Calls the "kql_query" MCP tool on the McpHost child process.
    /// Always returns a populated <see cref="KqlToolResponse"/>; never throws.
    /// On any failure, <c>Ok=false</c> and <c>Error</c> contains a
    /// <c>[ErrorType] message</c> string — no fabricated data.
    /// </summary>
    public async Task<KqlToolResponse> ExecuteAsync(
        KqlToolRequest  request,
        CancellationToken ct = default)
    {
        var executedAtUtc = DateTimeOffset.UtcNow;

        // Per-call timeout layered over any caller cancellation.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            var client = await GetOrCreateClientAsync(cts.Token);

            _logger.LogDebug(
                "Calling MCP kql_query | workspace={WorkspaceId} | timespan={Timespan}",
                request.WorkspaceIdOrName, request.TimespanIso8601);

            var result = await client.CallToolAsync(
                "kql_query",
                new Dictionary<string, object?>
                {
                    ["workspaceId"] = request.WorkspaceIdOrName,
                    ["kql"]         = request.Kql,
                    ["timespan"]    = request.TimespanIso8601,
                },
                cancellationToken: cts.Token);

            // The kql_query tool always returns a single TextContentBlock
            // containing a JSON object (ok, tables, error, …).
            var textBlock = result.Content
                .OfType<TextContentBlock>()
                .FirstOrDefault();

            if (textBlock is null || string.IsNullOrWhiteSpace(textBlock.Text))
            {
                _logger.LogError("MCP kql_query returned no text content.");
                return ErrorResponse(request, executedAtUtc,
                    "MCP kql_query returned no text content.", "EmptyResponse");
            }

            return ParseMcpResponse(textBlock.Text, request, executedAtUtc);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timed out (not cancelled by the caller).
            _logger.LogWarning(
                "MCP kql_query timed out after {Seconds}s | workspace={WorkspaceId}",
                _options.TimeoutSeconds, request.WorkspaceIdOrName);

            return ErrorResponse(request, executedAtUtc,
                $"MCP kql_query timed out after {_options.TimeoutSeconds}s.", "Timeout");
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the caller — propagate.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "MCP kql_query call failed | workspace={WorkspaceId}", request.WorkspaceIdOrName);

            // Invalidate the client so the next call gets a fresh child process.
            await InvalidateClientAsync();

            return ErrorResponse(request, executedAtUtc, ex.Message, ex.GetType().Name);
        }
    }

    // ── Client lifecycle ──────────────────────────────────────────────────────

    private async Task<SdkMcpClient> GetOrCreateClientAsync(CancellationToken ct)
    {
        // Fast path — already connected.
        if (_mcpClient is not null)
            return _mcpClient;

        await _initLock.WaitAsync(ct);
        try
        {
            // Double-check inside the lock.
            if (_mcpClient is not null)
                return _mcpClient;

            var workDir = _options.WorkingDirectory ?? DiscoverSolutionRoot();

            _logger.LogInformation(
                "Starting McpHost child process | executable={Exe} args={Args} workdir={WorkDir}",
                _options.Executable,
                string.Join(' ', _options.Arguments),
                workDir ?? "(inherited)");

            // ── Environment variable inheritance ──────────────────────────────
            // StdioClientTransport does NOT inherit the parent's environment by
            // default.  We must explicitly forward the variables that McpHost
            // needs — in particular, PATH (so `az` / `pwsh` are discoverable),
            // Azure identity token-cache directories, and the hosting-env flag
            // so the child picks up appsettings.Development.json.
            var envVars = BuildChildEnvironment();

            _logger.LogDebug(
                "Child process environment: {Keys}",
                string.Join(", ", envVars.Keys.OrderBy(k => k)));

            var transport = new StdioTransport(new StdioTransportOptions
            {
                Name                 = "OpsCopilotMcpHost",
                Command              = _options.Executable,
                Arguments            = _options.Arguments.ToList(),
                WorkingDirectory     = workDir,
                EnvironmentVariables = envVars,
            });

            _mcpClient = await SdkMcpClient.CreateAsync(transport, cancellationToken: ct);

            _logger.LogInformation("McpHost child process started and connected.");
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
                try   { await _mcpClient.DisposeAsync(); }
                catch { /* best-effort disposal of dead client */ }
                _mcpClient = null;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_mcpClient is not null)
        {
            try
            {
                await _mcpClient.DisposeAsync();
                _logger.LogInformation("McpHost child process disposed.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing McpHost child process.");
            }

            _mcpClient = null;
        }

        _initLock.Dispose();
    }

    // ── Response parsing ──────────────────────────────────────────────────────

    /// <summary>
    /// Parses the JSON string returned by the kql_query MCP tool into
    /// <see cref="KqlToolResponse"/>.
    ///
    /// The tool emits a tables-centric envelope:
    /// <code>
    /// {
    ///   "ok": bool,
    ///   "workspaceId": "...",
    ///   "executedQuery": "...",
    ///   "timespan": "...",
    ///   "executedAtUtc": "ISO-8601",
    ///   "tables": [{ "name": "...", "columns": [...], "rows": [[...]] }],
    ///   "error": null | "..."
    /// }
    /// </code>
    /// This method flattens all table rows into the flat
    /// <c>IReadOnlyList&lt;IReadOnlyDictionary&lt;string,object?&gt;&gt;</c>
    /// shape expected by <see cref="KqlToolResponse.Rows"/>.
    /// </summary>
    private KqlToolResponse ParseMcpResponse(
        string          json,
        KqlToolRequest  request,
        DateTimeOffset  executedAtUtc)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root      = doc.RootElement;

            var ok = root.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();

            var workspaceId   = TryString(root, "workspaceId")   ?? request.WorkspaceIdOrName;
            var executedQuery = TryString(root, "executedQuery")  ?? request.Kql;
            var timespan      = TryString(root, "timespan")       ?? request.TimespanIso8601;
            var errorText     = TryString(root, "error");

            // Parse executedAtUtc from tool envelope; fall back to caller timestamp.
            DateTimeOffset execAt = executedAtUtc;
            if (root.TryGetProperty("executedAtUtc", out var eatEl)
                && eatEl.TryGetDateTimeOffset(out var parsedAt))
                execAt = parsedAt;

            // Flatten all table rows → list of column→value dictionaries.
            var rows = new List<IReadOnlyDictionary<string, object?>>();

            if (root.TryGetProperty("tables", out var tablesEl)
                && tablesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var tableEl in tablesEl.EnumerateArray())
                {
                    if (!tableEl.TryGetProperty("columns", out var colsEl) ||
                        !tableEl.TryGetProperty("rows",    out var rowsEl))
                        continue;

                    // Build ordered column name list.
                    var columns = colsEl.EnumerateArray()
                        .Select(c => c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")
                        .ToArray();

                    foreach (var rowEl in rowsEl.EnumerateArray())
                    {
                        var dict = new Dictionary<string, object?>(columns.Length);
                        var i    = 0;
                        foreach (var cellEl in rowEl.EnumerateArray())
                        {
                            var colName   = i < columns.Length ? columns[i] : $"col{i}";
                            dict[colName] = ExtractCellValue(cellEl);
                            i++;
                        }
                        rows.Add(dict);
                    }
                }
            }

            return new KqlToolResponse(
                Ok:            ok,
                Rows:          rows,
                ExecutedQuery: executedQuery,
                WorkspaceId:   workspaceId,
                Timespan:      timespan,
                ExecutedAtUtc: execAt,
                Error:         errorText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse MCP kql_query response JSON.");
            return ErrorResponse(request, executedAtUtc,
                $"Failed to parse MCP response: {ex.Message}", "ParseError");
        }
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    private static string? TryString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var el) ? el.GetString() : null;

    /// <summary>
    /// Converts a JSON cell element to a native CLR value:
    /// null → null, bool → bool, number → long or double, string → string,
    /// everything else → raw JSON text (quoted objects/arrays).
    /// </summary>
    private static object? ExtractCellValue(JsonElement cell)
        => cell.ValueKind switch
        {
            JsonValueKind.Null   => null,
            JsonValueKind.True   => (object?)true,
            JsonValueKind.False  => false,
            JsonValueKind.Number => cell.TryGetInt64(out var l)  ? l
                                  : cell.TryGetDouble(out var d) ? d
                                  : (object?)cell.GetRawText(),
            JsonValueKind.String => cell.GetString(),
            _                    => cell.GetRawText(), // object/array → keep as JSON text
        };

    private static KqlToolResponse ErrorResponse(
        KqlToolRequest request,
        DateTimeOffset executedAtUtc,
        string         message,
        string         errorType)
        => new(
            Ok:            false,
            Rows:          Array.Empty<IReadOnlyDictionary<string, object?>>(),
            ExecutedQuery: request.Kql,
            WorkspaceId:   request.WorkspaceIdOrName,
            Timespan:      request.TimespanIso8601,
            ExecutedAtUtc: executedAtUtc,
            Error:         $"[{errorType}] {message}");

    /// <summary>
    /// Builds a dictionary of environment variables to forward to the McpHost
    /// child process.  StdioClientTransport does NOT inherit the parent env,
    /// so we must explicitly pass every variable the child needs:
    ///
    /// 1. OS-essential: PATH, PATHEXT, USERPROFILE, HOME, TEMP, TMP
    /// 2. Windows-critical: SystemRoot, SystemDrive, COMSPEC, windir
    ///    (cmd.exe / Python hang without these — az.cmd uses cmd.exe)
    /// 3. Windows user-profile: APPDATA, LOCALAPPDATA, HOMEDRIVE, HOMEPATH,
    ///    ProgramData, ProgramFiles, ProgramFiles(x86)
    /// 4. .NET runtime: DOTNET_ROOT, DOTNET_CLI_HOME
    /// 5. Azure identity: AZURE_CONFIG_DIR, AZURE_TENANT_ID, MSI_ENDPOINT,
    ///    MSI_SECRET, IDENTITY_ENDPOINT, IDENTITY_HEADER  (MI / Workload ID)
    /// 6. Hosting env: ASPNETCORE_ENVIRONMENT, DOTNET_ENVIRONMENT
    /// 7. App-specific: WORKSPACE_ID plus any AzureAuth__* variables
    /// </summary>
    internal static Dictionary<string, string?> BuildChildEnvironment()
    {
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // ── 1. Well-known variables (forward if set) ─────────────────────
        string[] wellKnown =
        [
            // OS essentials
            "PATH", "PATHEXT", "USERPROFILE", "HOME", "TEMP", "TMP",
            // Windows-critical: cmd.exe / Python / .NET hang without these
            "SystemRoot", "SystemDrive", "COMSPEC", "windir",
            // Windows user-profile dirs (Azure CLI config, token caches)
            "APPDATA", "LOCALAPPDATA", "HOMEDRIVE", "HOMEPATH", "ProgramData",
            "ProgramFiles", "ProgramFiles(x86)",
            // .NET runtime
            "DOTNET_ROOT", "DOTNET_CLI_HOME",
            // Azure Identity SDK — local CLI/PS credential cache
            "AZURE_CONFIG_DIR", "AZURE_TENANT_ID",
            // Azure Managed Identity (App Service / Container Apps)
            "MSI_ENDPOINT", "MSI_SECRET",
            "IDENTITY_ENDPOINT", "IDENTITY_HEADER",
            // Hosting environment → selects appsettings.Development.json
            "ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT",
            // App-specific workspace override
            "WORKSPACE_ID",
        ];

        foreach (var name in wellKnown)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
                env[name] = value;
        }

        // ── 2. AzureAuth__* variables (config-binding via env vars) ──────
        //    The McpHost reads AzureAuth:Mode, AzureAuth:TenantId etc.
        //    which map to env vars like AzureAuth__Mode.
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

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> to the first
    /// directory containing a <c>*.sln</c> file (solution root).
    /// Returns <c>null</c> in published container images where no .sln exists.
    /// </summary>
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
