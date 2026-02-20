using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpsCopilot.AgentRuns.Application.Extensions;
using OpsCopilot.AgentRuns.Infrastructure.Extensions;
using OpsCopilot.AgentRuns.Infrastructure.Persistence;
using OpsCopilot.AgentRuns.Presentation.Endpoints;
using OpsCopilot.AgentRuns.Presentation.Extensions;
using OpsCopilot.AlertIngestion.Application.Extensions;
using OpsCopilot.AlertIngestion.Presentation.Endpoints;
using OpsCopilot.AlertIngestion.Presentation.Extensions;
using OpsCopilot.BuildingBlocks.Infrastructure.Configuration;

// ─────────────────────────────────────────────────────────────────────────────
// OpsCopilot.ApiHost — public API surface
//
// Configuration loading order (standard ASP.NET Core):
//   1. appsettings.json
//   2. appsettings.{Environment}.json
//   3. User Secrets  (Development only — added automatically by the Web builder)
//   4. Environment Variables
//   5. Azure Key Vault (when KeyVault:VaultUri is set — any environment)
//
// Secrets required at runtime:
//   WORKSPACE_ID           — Log Analytics workspace GUID
//   SQL_CONNECTION_STRING  — Azure SQL connection string
// Never commit these to source control; see docs/local-dev-secrets.md.
//
// MCP hard-boundary: this host MUST NOT reference Azure.Monitor.Query or call
// Log Analytics directly. All KQL observations travel through McpHost via
// the MCP stdio protocol (StdioClientTransport → McpStdioKqlToolClient).
// Configure McpHost via the McpKql section or backward-compat env vars:
//   MCP_KQL_SERVER_COMMAND  (default: dotnet run --project src/Hosts/OpsCopilot.McpHost/...)
//   MCP_KQL_TIMEOUT_SECONDS (default: 30)
// ─────────────────────────────────────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);

// ── Bootstrap logger (before DI container is built) ──────────────────────────
// Used only for startup configuration messages.
using var bootstrapLoggerFactory = LoggerFactory.Create(lb =>
    lb.AddConsole().SetMinimumLevel(LogLevel.Information));
var startupLogger = bootstrapLoggerFactory.CreateLogger("Startup");

startupLogger.LogInformation(
    "[Startup] Environment: {Env}", builder.Environment.EnvironmentName);

// ── Azure Key Vault configuration provider ────────────────────────────────────
// At this point the builder has already loaded:
//   appsettings.json → appsettings.{env}.json → User Secrets (Dev) → env vars
// We peek KeyVault:VaultUri and conditionally add the Key Vault source.
// This means env vars / User Secrets can supply the vault URI itself.
builder.Configuration.AddOpsCopilotKeyVault(
    builder.Configuration["KeyVault:VaultUri"],
    startupLogger);

// ── Module registrations ──────────────────────────────────────────────────────
builder.Services
    // AgentRuns module
    .AddAgentRunsApplication()
    .AddAgentRunsInfrastructure(builder.Configuration)   // EF Core + IKqlToolClient
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

