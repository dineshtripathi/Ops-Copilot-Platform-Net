using Azure.Core;
using Azure.Identity;
using Azure.Monitor.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpsCopilot.Rag.Infrastructure.Extensions;

// ─────────────────────────────────────────────────────────────────────────────
// OpsCopilot.McpHost — MCP tool server (stdio transport)
//
// Protocol : Model Context Protocol (MCP) over stdin/stdout.
// Transport: stdio  — stdout is the MCP wire; stderr is for application logs.
// Tools    : kql_query — executes KQL against Azure Log Analytics.
//            runbook_search — searches the operational runbook knowledge base.
//
// Authentication modes (AzureAuth:Mode):
//   ExplicitChain          — deterministic credential chain; default in Development.
//   DefaultAzureCredential — full DAC with configurable exclusions; default in Production.
//
// How to run locally:
//   az login --tenant <your-tenant-id>
//   dotnet run --project src/Hosts/OpsCopilot.McpHost
//
// See docs/local-dev-auth.md for full troubleshooting.
// ─────────────────────────────────────────────────────────────────────────────

var builder = Host.CreateApplicationBuilder(args);

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

// ── RAG infrastructure (runbook retrieval for the runbook_search tool) ────────
builder.Services.AddRagInfrastructure(builder.Configuration);

// ── RAG diagnostics — log base path so runbook resolution issues are obvious ──
{
    var ragBasePath = builder.Configuration["Rag:RunbookBasePath"] ?? "(default — embedded)";
    startupLogger.LogInformation(
        "[Startup] RAG  RunbookBasePath={BasePath}",
        ragBasePath);
}

// ── MCP server ────────────────────────────────────────────────────────────────
// - StdioServerTransport: reads JSON-RPC from stdin, writes to stdout
// - WithToolsFromAssembly: discovers all [McpServerToolType] classes in this exe
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

startupLogger.LogInformation("[Startup] MCP server configured — stdio transport, tools from assembly");

// ── Development-only auth probe ─────────────────────────────────────────────
// Attempt to acquire a real token before the MCP wire starts. This surfaces
// credential problems (expired `az login`, wrong tenant, missing CLI) as a
// clear log message instead of a cryptic MCP tool-call failure later.
if (isDevelopment)
{
    const string logAnalyticsScope = "https://api.loganalytics.io/.default";
    try
    {
        startupLogger.LogInformation("[Auth-Probe] Acquiring token for {Scope} …", logAnalyticsScope);
        var tokenResult = await credential.GetTokenAsync(
            new TokenRequestContext(new[] { logAnalyticsScope }),
            CancellationToken.None);

        startupLogger.LogInformation(
            "[Auth-Probe] ✔ Token acquired — expires {ExpiresOn:u}",
            tokenResult.ExpiresOn);
    }
    catch (AuthenticationFailedException ex)
    {
        startupLogger.LogError(
            ex,
            "[Auth-Probe] ✘ Failed to acquire a token for {Scope}.  " +
            "Ensure you are logged in:  az login --tenant <tenant-id>  or  " +
            "Connect-AzAccount -TenantId <tenant-id>",
            logAnalyticsScope);
    }
    catch (Exception ex)
    {
        startupLogger.LogWarning(
            ex,
            "[Auth-Probe] ✘ Unexpected error during probe for {Scope}. " +
            "The MCP server will still start, but KQL tool calls may fail.",
            logAnalyticsScope);
    }
}

await builder.Build().RunAsync();

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


