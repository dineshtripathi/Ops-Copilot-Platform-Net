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
/// Implements <see cref="IRunbookSearchToolClient"/> by launching the
/// OpsCopilot.McpHost process and calling the "runbook_search" MCP tool over
/// stdio using the official ModelContextProtocol C# SDK.
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
/// Note: In this version the runbook client manages its own child process
/// independently from <see cref="McpStdioKqlToolClient"/>. Both point to the
/// same McpHost binary.  Consolidation into a shared process is a future
/// optimisation (post-3B).
///
/// Boundary constraint:
///   This class MUST NOT reference the Rag module.
///   All runbook retrieval happens inside McpHost.
/// </summary>
public sealed class McpStdioRunbookToolClient : IRunbookSearchToolClient, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private readonly McpKqlServerOptions                  _options;
    private readonly ILogger<McpStdioRunbookToolClient>   _logger;

    // Lazy MCP client — initialised once on first call, guarded by _initLock.
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SdkMcpClient? _mcpClient;
    private bool          _disposed;

    public McpStdioRunbookToolClient(
        McpKqlServerOptions                  options,
        ILogger<McpStdioRunbookToolClient>   logger)
    {
        _options = options;
        _logger  = logger;
    }

    // ── IRunbookSearchToolClient ──────────────────────────────────────────────

    /// <summary>
    /// Calls the "runbook_search" MCP tool on the McpHost child process.
    /// Always returns a populated <see cref="RunbookSearchToolResponse"/>; never throws.
    /// On any failure, <c>Ok=false</c> and <c>Error</c> contains a
    /// <c>[ErrorType] message</c> string — no fabricated data.
    /// </summary>
    public async Task<RunbookSearchToolResponse> ExecuteAsync(
        RunbookSearchToolRequest request,
        CancellationToken        ct = default)
    {
        // Per-call timeout layered over any caller cancellation.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            var client = await GetOrCreateClientAsync(cts.Token);

            _logger.LogDebug(
                "Calling MCP runbook_search | query={Query} | maxResults={MaxResults}",
                request.Query, request.MaxResults);

            var result = await client.CallToolAsync(
                "runbook_search",
                new Dictionary<string, object?>
                {
                    ["query"]      = request.Query,
                    ["maxResults"] = request.MaxResults,
                },
                cancellationToken: cts.Token);

            // The runbook_search tool returns a single TextContentBlock
            // containing a JSON envelope {ok, query, hitCount, hits, error}.
            var textBlock = result.Content
                .OfType<TextContentBlock>()
                .FirstOrDefault();

            if (textBlock is null || string.IsNullOrWhiteSpace(textBlock.Text))
            {
                _logger.LogError("MCP runbook_search returned no text content.");
                return ErrorResponse(request, "MCP runbook_search returned no text content.", "EmptyResponse");
            }

            return ParseMcpResponse(textBlock.Text, request);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "MCP runbook_search timed out after {Seconds}s | query={Query}",
                _options.TimeoutSeconds, request.Query);

            return ErrorResponse(request,
                $"MCP runbook_search timed out after {_options.TimeoutSeconds}s.", "Timeout");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "MCP runbook_search call failed | query={Query}", request.Query);

            await InvalidateClientAsync();

            return ErrorResponse(request, ex.Message, ex.GetType().Name);
        }
    }

    // ── Client lifecycle ──────────────────────────────────────────────────────

    private async Task<SdkMcpClient> GetOrCreateClientAsync(CancellationToken ct)
    {
        if (_mcpClient is not null)
            return _mcpClient;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_mcpClient is not null)
                return _mcpClient;

            var workDir = _options.WorkingDirectory ?? DiscoverSolutionRoot();

            _logger.LogInformation(
                "Starting McpHost child process (runbook) | executable={Exe} args={Args} workdir={WorkDir}",
                _options.Executable,
                string.Join(' ', _options.Arguments),
                workDir ?? "(inherited)");

            var envVars = McpStdioKqlToolClient.BuildChildEnvironment();

            var transport = new StdioTransport(new StdioTransportOptions
            {
                Name                 = "OpsCopilotMcpHost-Runbook",
                Command              = _options.Executable,
                Arguments            = _options.Arguments.ToList(),
                WorkingDirectory     = workDir,
                EnvironmentVariables = envVars,
            });

            _mcpClient = await SdkMcpClient.CreateAsync(transport, cancellationToken: ct);

            _logger.LogInformation("McpHost child process (runbook) started and connected.");
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
                _logger.LogInformation("McpHost child process (runbook) disposed.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing McpHost child process (runbook).");
            }

            _mcpClient = null;
        }

        _initLock.Dispose();
    }

    // ── Response parsing ──────────────────────────────────────────────────────

    /// <summary>
    /// Parses the JSON envelope returned by the runbook_search MCP tool:
    /// <code>
    /// {
    ///   "ok": bool,
    ///   "query": "...",
    ///   "hitCount": 3,
    ///   "hits": [{ "runbookId", "title", "snippet", "score" }],
    ///   "error": null | "..."
    /// }
    /// </code>
    /// </summary>
    private RunbookSearchToolResponse ParseMcpResponse(
        string                    json,
        RunbookSearchToolRequest  request)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root      = doc.RootElement;

            var ok    = root.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();
            var query = TryString(root, "query") ?? request.Query;
            var error = TryString(root, "error");

            var hits = new List<RunbookSearchHit>();

            if (root.TryGetProperty("hits", out var hitsEl)
                && hitsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var hitEl in hitsEl.EnumerateArray())
                {
                    var runbookId = TryString(hitEl, "runbookId") ?? "";
                    var title     = TryString(hitEl, "title")     ?? "";
                    var snippet   = TryString(hitEl, "snippet")   ?? "";
                    var score     = hitEl.TryGetProperty("score", out var s) && s.TryGetDouble(out var sv)
                        ? sv : 0.0;

                    hits.Add(new RunbookSearchHit(runbookId, title, snippet, score));
                }
            }

            return new RunbookSearchToolResponse(ok, hits, query, error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse MCP runbook_search response JSON.");
            return ErrorResponse(request,
                $"Failed to parse MCP response: {ex.Message}", "ParseError");
        }
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    private static string? TryString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var el) ? el.GetString() : null;

    private static RunbookSearchToolResponse ErrorResponse(
        RunbookSearchToolRequest request,
        string                   message,
        string                   errorType)
        => new(
            Ok:    false,
            Hits:  Array.Empty<RunbookSearchHit>(),
            Query: request.Query,
            Error: $"[{errorType}] {message}");

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
