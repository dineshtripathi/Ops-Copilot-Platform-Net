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

// ── Azure Monitor Query client (singleton) ────────────────────────────────────
// Uses DefaultAzureCredential:
//   Container Apps → system-assigned managed identity (RBAC already provisioned)
//   Local dev      → az login / AZURE_CLIENT_ID + AZURE_CLIENT_SECRET env vars
builder.Services.AddSingleton(_ =>
    new LogsQueryClient(new DefaultAzureCredential()));

// ── MCP server ────────────────────────────────────────────────────────────────
// - StdioServerTransport: reads JSON-RPC from stdin, writes to stdout
// - WithToolsFromAssembly: discovers all [McpServerToolType] classes in this exe
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();


