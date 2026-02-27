using Microsoft.Extensions.Logging;
using OpsCopilot.AgentRuns.Presentation.Endpoints;
using OpsCopilot.AgentRuns.Presentation.Extensions;
using OpsCopilot.AlertIngestion.Presentation.Endpoints;
using OpsCopilot.AlertIngestion.Presentation.Extensions;
using OpsCopilot.BuildingBlocks.Infrastructure.Configuration;
using OpsCopilot.Governance.Presentation.Extensions;
using OpsCopilot.SafeActions.Presentation.Endpoints;
using OpsCopilot.SafeActions.Presentation.Extensions;
using OpsCopilot.Reporting.Presentation.Endpoints;
using OpsCopilot.Reporting.Presentation.Extensions;
using OpsCopilot.Evaluation.Presentation.Endpoints;
using OpsCopilot.Evaluation.Presentation.Extensions;
using OpsCopilot.Connectors.Infrastructure.Extensions;
using OpsCopilot.Tenancy.Presentation.Extensions;

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

// ── Startup diagnostics — surface config problems before they become timeouts ─
{
    var mcpCmd     = builder.Configuration["McpKql:ServerCommand"]
                  ?? builder.Configuration["MCP_KQL_SERVER_COMMAND"]
                  ?? "(built-in default)";
    var mcpTimeout = builder.Configuration["McpKql:TimeoutSeconds"]
                  ?? builder.Configuration["MCP_KQL_TIMEOUT_SECONDS"]
                  ?? "30";
    var hasWsId    = !string.IsNullOrWhiteSpace(builder.Configuration["WORKSPACE_ID"]);
    var hasSql     = !string.IsNullOrWhiteSpace(
                         builder.Configuration["ConnectionStrings:Sql"]
                      ?? builder.Configuration["SQL_CONNECTION_STRING"]);

    startupLogger.LogInformation(
        "[Startup] McpKql  ServerCommand={Cmd} | TimeoutSeconds={Timeout}",
        mcpCmd, mcpTimeout);
    startupLogger.LogInformation(
        "[Startup] Config  WORKSPACE_ID={HasWsId} | SQL_CONNECTION_STRING={HasSql}",
        hasWsId ? "set" : "*** MISSING ***",
        hasSql  ? "set" : "*** MISSING ***");

    if (!hasWsId)
        startupLogger.LogWarning(
            "[Startup] WORKSPACE_ID is not configured. " +
            "POST /agent/triage will return 400 unless callers supply WorkspaceId in the request body. " +
            "Set via User Secrets: dotnet user-secrets set WORKSPACE_ID <guid>");

    var safeActionsExec = builder.Configuration.GetValue<bool>("SafeActions:EnableExecution");
    startupLogger.LogInformation(
        "[Startup] SafeActions EnableExecution={Enabled}", safeActionsExec);
}

// ── Module registrations ──────────────────────────────────────────────────────
// Each Presentation facade hides Application + Infrastructure wiring.
builder.Services
    .AddAgentRunsModule(builder.Configuration)
    .AddAlertIngestionModule()
    .AddGovernanceModule(builder.Configuration, startupLogger)
    .AddSafeActionsModule(builder.Configuration)
    .AddReportingModule(builder.Configuration)
    .AddEvaluationModule()
    .AddConnectorsModule()
    .AddTenancyModule(builder.Configuration);

// ── Observability ─────────────────────────────────────────────────────────────
builder.Logging.AddConsole();

var app = builder.Build();

// ── Database bootstrap ────────────────────────────────────────────────────────
await app.UseAgentRunsMigrations();
await app.UseSafeActionsMigrations();
await app.UseTenancyMigrations();

// ── Health probe ──────────────────────────────────────────────────────────────
app.MapGet("/healthz", () => Results.Ok("healthy"))
   .WithName("Health")
   .ExcludeFromDescription();

// ── Module endpoints ──────────────────────────────────────────────────────────
app.MapAlertIngestionEndpoints();   // POST /ingest/alert
app.MapAgentRunEndpoints();         // POST /agent/triage
app.MapSafeActionEndpoints();       // /safe-actions/*
app.MapReportingEndpoints();            // /reports/safe-actions/*
app.MapPlatformReportingEndpoints();    // /reports/platform/*
app.MapEvaluationEndpoints();           // /evaluation/*
app.MapTenancyEndpoints();              // /tenants/*

app.Run();

