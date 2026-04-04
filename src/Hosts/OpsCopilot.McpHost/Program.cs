using System.Runtime.InteropServices;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.Query;
using Azure.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpsCopilot.BuildingBlocks.Infrastructure.Configuration;
using OpsCopilot.McpHost;
using OpsCopilot.Rag.Presentation.Extensions;

// ─────────────────────────────────────────────────────────────────────────────
// OpsCopilot.McpHost — MCP tool server (HTTP or stdio transport)
//
// Transports:
//   HTTP (SSE) — primary; enabled when NOT spawned as a child process.
//                Listens on http://+:8081/mcp (matches Container Apps targetPort).
//   stdio       — fallback; enabled when stdin is redirected (child-process mode).
//                 Allows local dev clients to spawn McpHost directly without a
//                 running HTTP instance.
//
// Tools    : kql_query — executes KQL against Azure Log Analytics.
//            runbook_search — searches the operational runbook knowledge base.
//
// Authentication modes (AzureAuth:Mode):
//   ExplicitChain          — deterministic credential chain; default in Development.
//   DefaultAzureCredential — full DAC with configurable exclusions; default in Production.
//
// How to run locally (HTTP mode):
//   az login --tenant <your-tenant-id>
//   dotnet run --project src/Hosts/OpsCopilot.McpHost
//   # MCP endpoint:  http://localhost:8081/mcp
//
// See docs/local-dev-auth.md for full troubleshooting.
// ─────────────────────────────────────────────────────────────────────────────

// ── Transport mode detection ──────────────────────────────────────────────────
// When clients spawn McpHost as a child process they redirect stdin; that is
// the reliable signal that we are in stdio-pipe mode.  All other invocations
// (Azure Container Apps, manual dotnet run) use HTTP transport on port 8081.
//
// IMPORTANT: Azure Container Apps also redirects stdin (for log capture), so
// Console.IsInputRedirected returns true in the container — a false positive.
// Guard against this by checking the Container Apps identity env var.
bool isInContainerApp = !string.IsNullOrEmpty(
    Environment.GetEnvironmentVariable("CONTAINER_APP_NAME"));
bool isStdioPipeMode = Console.IsInputRedirected && !isInContainerApp;

// ── Win32 stdin handle non-inheritance guard ──────────────────────────────────
// Azure.Identity's AzureCliCredential (and AzurePowerShellCredential) spawn child
// processes WITHOUT redirecting stdin, so they would inherit McpHost's stdin
// handle — the live MCP protocol pipe.  The .NET MCP SDK reads that same handle
// concurrently (reading ahead for the next JSON-RPC message while a tool runs),
// so az.exe / pwsh.exe race the SDK for MCP data instead of receiving nothing or
// a clean EOF, causing Python / pwsh to block indefinitely.
//
// Fix: mark the Win32 STDIN handle non-inheritable right at process start.
// This does NOT affect McpHost's own Console.In reading — the handle stays open
// and fully usable for the MCP SDK.  Only new child processes will no longer
// inherit it.  On non-Windows this is a no-op.
if (OperatingSystem.IsWindows())
{
    var hIn = Win32Stdin.GetStdHandle(Win32Stdin.StdInputHandle);
    if (hIn != IntPtr.Zero && hIn != Win32Stdin.InvalidHandleValue)
        Win32Stdin.SetHandleInformation(hIn, Win32Stdin.HandleFlagInherit, 0);
}

var builder = WebApplication.CreateBuilder(args);

// ── HTTP port — listen on 8081 to match Container Apps targetPort ─────────────
// In stdio-pipe mode use an ephemeral loopback port (OS-assigned) so that
// spawning multiple child processes never causes a port conflict.
builder.WebHost.UseUrls(isStdioPipeMode ? "http://127.0.0.1:0" : "http://+:8081");

// ── Logging → all to stderr so stdout remains clean for MCP wire ──────────────
builder.Logging.AddConsole(o =>
{
    // Every log level (Trace and above) goes to stderr.
    o.LogToStandardErrorThreshold = LogLevel.Trace;
});

using var bootstrapLoggerFactory = LoggerFactory.Create(lb =>
    lb.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace));
var startupLogger = bootstrapLoggerFactory.CreateLogger("Startup");

bool isDevelopment = builder.Environment.IsDevelopment();

builder.Configuration.AddOpsCopilotKeyVault(
    builder.Configuration["KeyVault:VaultUri"],
    startupLogger);

// ── Azure credential configuration ──────────────────────────────────────────
// AzureAuth:Mode selects the strategy:
//   "ExplicitChain"          → ChainedTokenCredential built from Use* flags.
//   "DefaultAzureCredential" → SDK DefaultAzureCredential with Exclude* flags.
//
// When Mode is not set, the default is:
//   Development → ExplicitChain  (deterministic, no noise sources)
//   Production  → DefaultAzureCredential (MI-only by default)
string? tenantId = builder.Configuration["AzureAuth:TenantId"];
string authMode  = builder.Configuration["AzureAuth:Mode"]
                   ?? (isDevelopment ? "ExplicitChain" : "DefaultAzureCredential");

if (isDevelopment && string.IsNullOrEmpty(tenantId))
{
    startupLogger.LogWarning(
        "[Auth] AzureAuth:TenantId is empty.  " +
        "Azure CLI / PowerShell credentials will use whichever tenant you last " +
        "logged into, which may not be the OpsCopilot tenant.  " +
        "Set it in appsettings.Development.json or via: " +
        "AzureAuth__TenantId=<your-tenant-guid>");
}

startupLogger.LogInformation(
    "[Auth] Mode={AuthMode} | Environment={Env}",
    authMode, builder.Environment.EnvironmentName);

TokenCredential credential;

if (string.Equals(authMode, "ExplicitChain", StringComparison.OrdinalIgnoreCase))
{
    // ── ExplicitChain: deterministic credential list ─────────────────────
    //
    // IMPORTANT: ChainedTokenCredential only falls through on CredentialUnavailableException.
    // However, when credentials like AzureCliCredential are constructed manually (not via
    // DefaultAzureCredentialFactory), the internal IsChainedCredential flag is false.
    // This means timeouts throw AuthenticationFailedException — which is FATAL to the chain.
    //
    // Fix: We wrap each credential in a ResilientCredential that converts
    // AuthenticationFailedException → CredentialUnavailableException so the chain falls through.
    //
    // We also keep ProcessTimeout at the SDK default (13 s) rather than 60 s.
    // 60 s was unnecessarily long and made the pipe-child `az` hang painful.

    bool useCli   = ReadBool(builder.Configuration["AzureAuth:UseAzureCliCredential"], true);
    bool usePs    = ReadBool(builder.Configuration["AzureAuth:UseAzurePowerShellCredential"], true);
    bool useAzd   = ReadBool(builder.Configuration["AzureAuth:UseAzureDeveloperCliCredential"], false);
    int  timeoutS = ReadInt(builder.Configuration["AzureAuth:CredentialProcessTimeoutSeconds"], 13);
    var  timeout  = TimeSpan.FromSeconds(timeoutS);

    var sources     = new List<TokenCredential>();
    var sourceNames = new List<string>();

    if (useCli)
    {
        sources.Add(new ResilientCredential(
            "AzureCliCredential",
            new AzureCliCredential(new AzureCliCredentialOptions
            {
                TenantId       = tenantId,
                ProcessTimeout = timeout,
            }),
            startupLogger));
        sourceNames.Add("AzureCliCredential");
    }

    if (usePs)
    {
        sources.Add(new ResilientCredential(
            "AzurePowerShellCredential",
            new AzurePowerShellCredential(new AzurePowerShellCredentialOptions
            {
                TenantId       = tenantId,
                ProcessTimeout = timeout,
            }),
            startupLogger));
        sourceNames.Add("AzurePowerShellCredential");
    }

    if (useAzd)
    {
        sources.Add(new ResilientCredential(
            "AzureDeveloperCliCredential",
            new AzureDeveloperCliCredential(new AzureDeveloperCliCredentialOptions
            {
                TenantId       = tenantId,
                ProcessTimeout = timeout,
            }),
            startupLogger));
        sourceNames.Add("AzureDeveloperCliCredential");
    }

    if (sources.Count == 0)
    {
        throw new InvalidOperationException(
            "AzureAuth:Mode is ExplicitChain but no credentials are enabled. " +
            "Set at least one of AzureAuth:UseAzureCliCredential, " +
            "AzureAuth:UseAzurePowerShellCredential, or " +
            "AzureAuth:UseAzureDeveloperCliCredential to true.");
    }

    credential = new ChainedTokenCredential(sources.ToArray());

    startupLogger.LogInformation(
        "[Auth] ExplicitChain configured | Chain={Chain} | " +
        "TenantId={TenantId} | ProcessTimeoutSeconds={Timeout}",
        string.Join(" → ", sourceNames),
        string.IsNullOrEmpty(tenantId) ? "(default)" : tenantId,
        timeoutS);
}
else
{
    // ── DefaultAzureCredential: production / MI-safe ────────────────────
    // Container Apps provides a system-assigned managed identity — that is
    // usually the only source needed.  Override via config to widen the chain.
    var options = new DefaultAzureCredentialOptions
    {
        ExcludeEnvironmentCredential        = ReadBool(builder.Configuration["AzureAuth:ExcludeEnvironmentCredential"], true),
        ExcludeWorkloadIdentityCredential   = ReadBool(builder.Configuration["AzureAuth:ExcludeWorkloadIdentityCredential"], true),
        ExcludeManagedIdentityCredential    = ReadBool(builder.Configuration["AzureAuth:ExcludeManagedIdentityCredential"], false),
        ExcludeSharedTokenCacheCredential   = ReadBool(builder.Configuration["AzureAuth:ExcludeSharedTokenCacheCredential"], true),
        ExcludeVisualStudioCredential       = ReadBool(builder.Configuration["AzureAuth:ExcludeVisualStudioCredential"], true),
        ExcludeVisualStudioCodeCredential   = ReadBool(builder.Configuration["AzureAuth:ExcludeVisualStudioCodeCredential"], true),
        ExcludeAzureCliCredential           = ReadBool(builder.Configuration["AzureAuth:ExcludeAzureCliCredential"], true),
        ExcludeAzurePowerShellCredential    = ReadBool(builder.Configuration["AzureAuth:ExcludeAzurePowerShellCredential"], true),
        ExcludeAzureDeveloperCliCredential  = ReadBool(builder.Configuration["AzureAuth:ExcludeAzureDeveloperCliCredential"], true),
        ExcludeInteractiveBrowserCredential = ReadBool(builder.Configuration["AzureAuth:ExcludeInteractiveBrowserCredential"], true),
    };

    if (!string.IsNullOrEmpty(tenantId))
        options.TenantId = tenantId;

    credential = new DefaultAzureCredential(options);

    startupLogger.LogInformation(
        "[Auth] DefaultAzureCredential configured | " +
        "ExcludeManagedIdentity={MI} | ExcludeCli={CLI} | ExcludePS={PS} | " +
        "TenantId={TenantId}",
        options.ExcludeManagedIdentityCredential,
        options.ExcludeAzureCliCredential,
        options.ExcludeAzurePowerShellCredential,
        string.IsNullOrEmpty(options.TenantId) ? "(default)" : options.TenantId);
}

// ── Azure Monitor Query client (singleton) ────────────────────────────────────
builder.Services.AddSingleton(_ => new LogsQueryClient(credential));

// ── Azure Resource Manager client (singleton, for deployment_diff tool) ───────
builder.Services.AddSingleton(_ => new ArmClient(credential));

// ── RAG module (runbook retrieval for the runbook_search tool) ────────────────
builder.Services.AddRagModule(builder.Configuration);

// ── HTTP client factory (for safe-actions and future REST tool calls) ────────
builder.Services.AddHttpClient();

// ── Health probes — /healthz/live and /healthz/ready for Container Apps ───────
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("McpHost is responsive"));

// ── RAG diagnostics — log base path so runbook resolution issues are obvious ──
{
    var ragBasePath = builder.Configuration["Rag:RunbookBasePath"] ?? "(default — embedded)";
    startupLogger.LogInformation(
        "[Startup] RAG  RunbookBasePath={BasePath}",
        ragBasePath);
}

// ── MCP server ────────────────────────────────────────────────────────────────
// - HTTP transport (MapMcp below): SSE endpoint at /mcp — used in Azure and
//   by any client that sets McpKql:ServerUrl to the McpHost HTTP address.
// - Stdio transport (conditional): also registered when stdin is redirected
//   (child-process mode) so local dev clients can spawn McpHost directly.
// - WithToolsFromAssembly: discovers all [McpServerToolType] classes in this exe
var mcpBuilder = builder.Services
    .AddMcpServer()
    .WithToolsFromAssembly();

if (isStdioPipeMode)
    mcpBuilder.WithStdioServerTransport();
else
    mcpBuilder.WithHttpTransport();

startupLogger.LogInformation(
    "[Startup] MCP server configured — {Transport} transport, tools from assembly",
    isStdioPipeMode ? "stdio" : "HTTP (SSE at /mcp)");

// ── Development-only auth probe ─────────────────────────────────────────────
// Attempt to acquire real tokens before the MCP wire starts. This surfaces
// credential problems (expired `az login`, wrong tenant, missing CLI) as a
// clear log message instead of a cryptic MCP tool-call failure later.
// Probing both scopes here also pre-warms the MSAL token cache so that
// concurrent tool calls (KQL + list_subscriptions) do not race on the
// first `az account get-access-token` invocation and hit cache-lock contention.
if (isDevelopment)
{
    string[] probedScopes =
    [
        "https://api.loganalytics.io/.default",
        "https://management.azure.com/.default",
    ];

    foreach (var scope in probedScopes)
    {
        try
        {
            startupLogger.LogInformation("[Auth-Probe] Acquiring token for {Scope} …", scope);
            var tokenResult = await credential.GetTokenAsync(
                new TokenRequestContext(new[] { scope }),
                CancellationToken.None);

            startupLogger.LogInformation(
                "[Auth-Probe] ✔ Token acquired for {Scope} — expires {ExpiresOn:u}",
                scope, tokenResult.ExpiresOn);
        }
        catch (AuthenticationFailedException ex)
        {
            startupLogger.LogError(
                ex,
                "[Auth-Probe] ✘ Failed to acquire a token for {Scope}.  " +
                "Ensure you are logged in:  az login --tenant <tenant-id>  or  " +
                "Connect-AzAccount -TenantId <tenant-id>",
                scope);
        }
        catch (Exception ex)
        {
            startupLogger.LogWarning(
                ex,
                "[Auth-Probe] ✘ Unexpected error during probe for {Scope}. " +
                "The MCP server will still start, but tool calls may fail.",
                scope);
        }
    }
}

var app = builder.Build();

// ── McpAuth: inbound API-key validation (HTTP mode only) ─────────────────────
// When McpAuth:ApiKey is configured, all inbound HTTP requests must present
// the key via Authorization: Bearer <key> or X-Api-Key: <key>.
// When the key is empty (default), a startup warning is logged and the
// endpoint remains open — dev-safe fallback. The key value is never logged.
if (!isStdioPipeMode)
{
    var mcpAuthKey = app.Configuration["McpAuth:ApiKey"];
    if (string.IsNullOrEmpty(mcpAuthKey))
    {
        startupLogger.LogWarning(
            "[McpAuth] McpAuth:ApiKey is not configured — the /mcp endpoint " +
            "is open to all callers. Set McpAuth:ApiKey in configuration " +
            "to enable inbound authentication.");
    }
    else
    {
        startupLogger.LogInformation(
            "[McpAuth] API-key authentication enabled for /mcp endpoint.");
    }

    app.UseMiddleware<McpApiKeyMiddleware>();
}

// Register the MCP SSE endpoint and health probes in HTTP mode only.
// In stdio-pipe mode the server communicates over stdin/stdout; MapMcp requires
// HTTP transport services and must not be called in stdio mode.
if (!isStdioPipeMode)
{
    app.MapHealthChecks("/healthz/live").AllowAnonymous();
    app.MapHealthChecks("/healthz/ready").AllowAnonymous();
    app.MapHealthChecks("/healthz").AllowAnonymous();
    app.MapMcp("/mcp");
}

await app.RunAsync();

static bool ReadBool(string? raw, bool defaultValue)
    => bool.TryParse(raw, out var parsed) ? parsed : defaultValue;

static int ReadInt(string? raw, int defaultValue)
    => int.TryParse(raw, out var parsed) ? parsed : defaultValue;

// ═════════════════════════════════════════════════════════════════════════════
// ResilientCredential – works around the internal IsChainedCredential flag
// ═════════════════════════════════════════════════════════════════════════════
//
// Azure.Identity's ChainedTokenCredential only falls through to the next
// credential on CredentialUnavailableException.  When credentials like
// AzureCliCredential timeout, they throw AuthenticationFailedException —
// UNLESS the internal IsChainedCredential flag is set to true.
//
// DefaultAzureCredentialFactory sets that flag, but manual construction
// (which ExplicitChain mode uses) cannot — the property is internal.
//
// This wrapper catches AuthenticationFailedException and converts it to
// CredentialUnavailableException, ensuring ChainedTokenCredential falls
// through to the next credential in the chain.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Wraps a <see cref="TokenCredential"/> so that any
/// <see cref="AuthenticationFailedException"/> is re-thrown as
/// <see cref="CredentialUnavailableException"/>, allowing
/// <see cref="ChainedTokenCredential"/> to try the next credential.
/// </summary>
// ── Win32 P/Invoke helpers (stdin handle non-inheritance) ───────────────────
file static class Win32Stdin
{
    internal const int  StdInputHandle     = -10;
    internal const uint HandleFlagInherit  = 0x00000001;
    internal static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetHandleInformation(
        IntPtr hObject, uint dwMask, uint dwFlags);
}

internal sealed class ResilientCredential : TokenCredential
{
    private readonly string          _name;
    private readonly TokenCredential _inner;
    private readonly ILogger         _logger;

    public ResilientCredential(string name, TokenCredential inner, ILogger logger)
    {
        _name   = name;
        _inner  = inner;
        _logger = logger;
    }

    public override AccessToken GetToken(
        TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        try
        {
            return _inner.GetToken(requestContext, cancellationToken);
        }
        catch (AuthenticationFailedException ex)
        {
            _logger.LogWarning(
                ex,
                "[ResilientCredential] {Name} threw AuthenticationFailedException — " +
                "converting to CredentialUnavailableException so the chain falls through.",
                _name);

            throw new CredentialUnavailableException(
                $"{_name} failed: {ex.Message}", ex);
        }
    }

    public override async ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        try
        {
            return await _inner.GetTokenAsync(requestContext, cancellationToken);
        }
        catch (AuthenticationFailedException ex)
        {
            _logger.LogWarning(
                ex,
                "[ResilientCredential] {Name} threw AuthenticationFailedException — " +
                "converting to CredentialUnavailableException so the chain falls through.",
                _name);

            throw new CredentialUnavailableException(
                $"{_name} failed: {ex.Message}", ex);
        }
    }
}


