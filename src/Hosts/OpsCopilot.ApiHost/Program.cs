using Microsoft.EntityFrameworkCore;
using OpsCopilot.AgentRuns.Application.Extensions;
using OpsCopilot.AgentRuns.Infrastructure.Extensions;
using OpsCopilot.AgentRuns.Infrastructure.Persistence;
using OpsCopilot.AgentRuns.Presentation.Endpoints;
using OpsCopilot.AgentRuns.Presentation.Extensions;
using OpsCopilot.AlertIngestion.Application.Extensions;
using OpsCopilot.AlertIngestion.Presentation.Endpoints;
using OpsCopilot.AlertIngestion.Presentation.Extensions;

// ─────────────────────────────────────────────────────────────────────────────
// OpsCopilot.ApiHost — public API surface
//
// MCP hard-boundary: this host must NOT reference Azure.Monitor.Query or call
// Log Analytics directly.  All KQL observations travel through McpHost via
// the MCP stdio protocol (StdioClientTransport → McpStdioKqlToolClient).
//
// McpHost is launched as a child process on first tool call.  Configure via:
//   MCP_KQL_SERVER_COMMAND  (default: dotnet run --project src/Hosts/OpsCopilot.McpHost/...)
//   MCP_KQL_SERVER_WORKDIR  (default: auto-discovered solution root)
//   MCP_KQL_TIMEOUT_SECONDS (default: 30)
// ─────────────────────────────────────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);

// ── Module registrations ──────────────────────────────────────────────────────
builder.Services
    // AgentRuns module
    .AddAgentRunsApplication()
    .AddAgentRunsInfrastructure(builder.Configuration)   // EF Core + IAgentRunRepository + IKqlToolClient
    .AddAgentRunsPresentation()
    // AlertIngestion module
    .AddAlertIngestionApplication()
    .AddAlertIngestionPresentation();

// ── Observability ─────────────────────────────────────────────────────────────
builder.Logging.AddConsole();

var app = builder.Build();

// ── Database bootstrap ────────────────────────────────────────────────────────
// EnsureCreatedAsync() creates the agentRuns schema + tables on first run.
// No migrations needed for Dev Slice 1.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AgentRunsDbContext>();
    await db.Database.EnsureCreatedAsync();
    app.Logger.LogInformation("AgentRunsDbContext schema ensured.");
}

// ── Health probe ──────────────────────────────────────────────────────────────
app.MapGet("/healthz", () => Results.Ok("healthy"))
   .WithName("Health")
   .ExcludeFromDescription();

// ── Module endpoints ──────────────────────────────────────────────────────────
app.MapAlertIngestionEndpoints();   // POST /ingest/alert
app.MapAgentRunEndpoints();         // POST /agent/triage

app.Run();

