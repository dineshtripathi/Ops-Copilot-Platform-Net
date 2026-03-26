using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using SdkMcpClient = ModelContextProtocol.Client.McpClient;
using StdioTransport = ModelContextProtocol.Client.StdioClientTransport;
using StdioTransportOptions = ModelContextProtocol.Client.StdioClientTransportOptions;

namespace OpsCopilot.Reporting.Infrastructure.McpClient;

/// <summary>
/// Manages a single OpsCopilot.McpHost child process for the Reporting module.
///
/// Shared by <see cref="McpDeploymentSource"/> and <see cref="McpTenantEstateProvider"/>
/// so both use a single child process rather than spawning duplicates.
///
/// The child process is started lazily on the first <see cref="CallToolAsync"/> call
/// and reused for the application lifetime. On transport failure the client is
/// invalidated; the next call starts a fresh process.
///
/// Boundary rule: this class MUST NOT reference Azure.ResourceManager.
/// </summary>
internal sealed class ReportingMcpHostClient : IReportingMcpHostClient, IAsyncDisposable
{
    private readonly McpHostOptions _options;
    private readonly ILogger<ReportingMcpHostClient> _logger;

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SdkMcpClient? _mcpClient;
    private bool _disposed;

    public ReportingMcpHostClient(
        McpHostOptions                      options,
        ILogger<ReportingMcpHostClient>     logger)
    {
        _options = options;
        _logger  = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls an MCP tool on the McpHost child process.
    /// Never throws — returns a JSON error envelope on any failure.
    /// </summary>
    public async Task<string> CallToolAsync(
        string                      toolName,
        Dictionary<string, object?> args,
        CancellationToken           ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            var client = await GetOrCreateClientAsync(cts.Token);
            var result = await client.CallToolAsync(toolName, args, cancellationToken: cts.Token);
            var text   = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            return text ?? "{}";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Reporting McpHost tool {Tool} timed out after {Seconds}s.",
                toolName, _options.TimeoutSeconds);
            await InvalidateClientAsync();
            return $"{{\"ok\":false,\"error\":\"[Timeout] Tool '{toolName}' timed out after {_options.TimeoutSeconds}s.\"}}";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reporting McpHost tool {Tool} failed.", toolName);
            await InvalidateClientAsync();
            var msg = ex.Message.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"{{\"ok\":false,\"error\":\"[{ex.GetType().Name}] {msg}\"}}";
        }
    }

    // ── Client lifecycle ──────────────────────────────────────────────────────

    private async Task<SdkMcpClient> GetOrCreateClientAsync(CancellationToken ct)
    {
        if (_mcpClient is not null) return _mcpClient;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_mcpClient is not null) return _mcpClient;

            var workDir = _options.WorkingDirectory ?? DiscoverSolutionRoot();

            _logger.LogInformation(
                "Starting Reporting McpHost child process | exe={Exe} workdir={WorkDir}",
                _options.Executable, workDir ?? "(inherited)");

            var transport = new StdioTransport(new StdioTransportOptions
            {
                Name                 = "ReportingMcpHost",
                Command              = _options.Executable,
                Arguments            = _options.Arguments.ToList(),
                WorkingDirectory     = workDir,
                EnvironmentVariables = BuildChildEnvironment(),
            });

            _mcpClient = await SdkMcpClient.CreateAsync(transport, cancellationToken: ct);
            _logger.LogInformation("Reporting McpHost child process started and connected.");
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
                catch { /* best-effort disposal of failed client */ }
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
            try   { await _mcpClient.DisposeAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error disposing Reporting McpHost child process."); }
            _mcpClient = null;
        }

        _initLock.Dispose();
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds environment variables to forward to the McpHost child process.
    /// StdioClientTransport does NOT inherit the parent's environment by default.
    /// Forwards credentials, PATH, and hosting-env variables required by McpHost.
    /// </summary>
    internal static Dictionary<string, string?> BuildChildEnvironment()
    {
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // ── Well-known variables ──────────────────────────────────────────────
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

        // ── AzureAuth__* variables (config-binding via env vars) ──────────────
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
    /// Walks up from <see cref="AppContext.BaseDirectory"/> until a directory
    /// containing a <c>*.sln</c> file is found. Returns <c>null</c> in published
    /// container images where no .sln exists.
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
