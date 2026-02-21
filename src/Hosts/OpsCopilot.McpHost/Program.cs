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
// How to run locally:
//   az login                                    # authenticate with DefaultAzureCredential
//   dotnet run --project src/Hosts/OpsCopilot.McpHost
//
// How an MCP client connects (e.g. Claude Desktop, ApiHost future slice):
//   {
//     "command": "dotnet",
//     "args": ["run", "--project", "src/Hosts/OpsCopilot.McpHost"]
//   }
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
// Development : ChainedTokenCredential with explicit CLI / PowerShell sources
//               and a configurable ProcessTimeout — no noise credentials.
// Production  : DefaultAzureCredential narrowed to ManagedIdentity only
//               (override via AzureAuth:* config if needed).
string? tenantId = builder.Configuration["AzureAuth:TenantId"];
TokenCredential credential;

if (isDevelopment)
{
    int timeoutSeconds = ReadInt(
        builder.Configuration["AzureAuth:CredentialProcessTimeoutSeconds"], 30);
    var processTimeout = TimeSpan.FromSeconds(timeoutSeconds);

    bool useCli = !ReadBool(
        builder.Configuration["AzureAuth:ExcludeAzureCliCredential"], false);
    bool usePs  = !ReadBool(
        builder.Configuration["AzureAuth:ExcludeAzurePowerShellCredential"], false);

    var sources = new List<TokenCredential>();

    if (useCli)
    {
        sources.Add(new AzureCliCredential(new AzureCliCredentialOptions
        {
            TenantId       = tenantId,
            ProcessTimeout = processTimeout,
        }));
    }

    if (usePs)
    {
        sources.Add(new AzurePowerShellCredential(new AzurePowerShellCredentialOptions
        {
            TenantId       = tenantId,
            ProcessTimeout = processTimeout,
        }));
    }

    if (sources.Count == 0)
    {
        throw new InvalidOperationException(
            "No Azure credentials enabled for Development. " +
            "Set AzureAuth:ExcludeAzureCliCredential and/or " +
            "AzureAuth:ExcludeAzurePowerShellCredential to false in " +
            "appsettings.Development.json.");
    }

    credential = new ChainedTokenCredential(sources.ToArray());

    startupLogger.LogInformation(
        "[Auth] Development credential chain | " +
        "AzureCli={UseCli} | AzurePowerShell={UsePs} | " +
        "TenantId={TenantId} | ProcessTimeoutSeconds={Timeout}",
        useCli, usePs,
        string.IsNullOrEmpty(tenantId) ? "(default)" : tenantId,
        timeoutSeconds);
}
else
{
    // Production / Staging: DefaultAzureCredential with most sources excluded.
    // Container Apps provides a system-assigned managed identity — that is
    // usually the only source needed.  Override via config to widen the chain.
    var options = new DefaultAzureCredentialOptions
    {
        ExcludeEnvironmentCredential         = ReadBool(builder.Configuration["AzureAuth:ExcludeEnvironmentCredential"], true),
        ExcludeWorkloadIdentityCredential    = ReadBool(builder.Configuration["AzureAuth:ExcludeWorkloadIdentityCredential"], true),
        ExcludeManagedIdentityCredential     = ReadBool(builder.Configuration["AzureAuth:ExcludeManagedIdentityCredential"], false),
        ExcludeSharedTokenCacheCredential    = ReadBool(builder.Configuration["AzureAuth:ExcludeSharedTokenCacheCredential"], true),
        ExcludeVisualStudioCredential        = ReadBool(builder.Configuration["AzureAuth:ExcludeVisualStudioCredential"], true),
        ExcludeVisualStudioCodeCredential    = ReadBool(builder.Configuration["AzureAuth:ExcludeVisualStudioCodeCredential"], true),
        ExcludeAzureCliCredential            = ReadBool(builder.Configuration["AzureAuth:ExcludeAzureCliCredential"], true),
        ExcludeAzurePowerShellCredential     = ReadBool(builder.Configuration["AzureAuth:ExcludeAzurePowerShellCredential"], true),
        ExcludeAzureDeveloperCliCredential   = ReadBool(builder.Configuration["AzureAuth:ExcludeAzureDeveloperCliCredential"], true),
        ExcludeInteractiveBrowserCredential  = ReadBool(builder.Configuration["AzureAuth:ExcludeInteractiveBrowserCredential"], true),
    };

    if (!string.IsNullOrEmpty(tenantId))
        options.TenantId = tenantId;

    credential = new DefaultAzureCredential(options);

    startupLogger.LogInformation(
        "[Auth] Production credential (DefaultAzureCredential) | " +
        "ExcludeManagedIdentity={MI} | TenantId={TenantId}",
        options.ExcludeManagedIdentityCredential,
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


