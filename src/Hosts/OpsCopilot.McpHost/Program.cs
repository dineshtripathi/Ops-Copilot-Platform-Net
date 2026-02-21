using Azure.Core;
using Azure.Identity;
using Azure.Monitor.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// ─────────────────────────────────────────────────────────────────────────────
// OpsCopilot.McpHost — MCP tool server (stdio transport)
//
// Protocol : Model Context Protocol (MCP) over stdin/stdout.
// Transport: stdio  — stdout is the MCP wire; stderr is for application logs.
// Tools    : kql_query — executes KQL against Azure Log Analytics.
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

startupLogger.LogInformation(
    "[Auth] Mode={AuthMode} | Environment={Env}",
    authMode, builder.Environment.EnvironmentName);

TokenCredential credential;

if (string.Equals(authMode, "ExplicitChain", StringComparison.OrdinalIgnoreCase))
{
    // ── ExplicitChain: deterministic credential list ─────────────────────
    bool useCli   = ReadBool(builder.Configuration["AzureAuth:UseAzureCliCredential"], true);
    bool usePs    = ReadBool(builder.Configuration["AzureAuth:UseAzurePowerShellCredential"], true);
    bool useAzd   = ReadBool(builder.Configuration["AzureAuth:UseAzureDeveloperCliCredential"], false);
    int  timeoutS = ReadInt(builder.Configuration["AzureAuth:CredentialProcessTimeoutSeconds"], 60);
    var  timeout  = TimeSpan.FromSeconds(timeoutS);

    var sources     = new List<TokenCredential>();
    var sourceNames = new List<string>();

    if (useCli)
    {
        sources.Add(new AzureCliCredential(new AzureCliCredentialOptions
        {
            TenantId       = tenantId,
            ProcessTimeout = timeout,
        }));
        sourceNames.Add("AzureCliCredential");
    }

    if (usePs)
    {
        sources.Add(new AzurePowerShellCredential(new AzurePowerShellCredentialOptions
        {
            TenantId       = tenantId,
            ProcessTimeout = timeout,
        }));
        sourceNames.Add("AzurePowerShellCredential");
    }

    if (useAzd)
    {
        sources.Add(new AzureDeveloperCliCredential(new AzureDeveloperCliCredentialOptions
        {
            TenantId       = tenantId,
            ProcessTimeout = timeout,
        }));
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

// ── MCP server ────────────────────────────────────────────────────────────────
// - StdioServerTransport: reads JSON-RPC from stdin, writes to stdout
// - WithToolsFromAssembly: discovers all [McpServerToolType] classes in this exe
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

static bool ReadBool(string? raw, bool defaultValue)
    => bool.TryParse(raw, out var parsed) ? parsed : defaultValue;

static int ReadInt(string? raw, int defaultValue)
    => int.TryParse(raw, out var parsed) ? parsed : defaultValue;


