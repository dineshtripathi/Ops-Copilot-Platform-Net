using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpsCopilot.AgentRuns.Presentation.Endpoints;
using OpsCopilot.AgentRuns.Presentation.Extensions;
using OpsCopilot.AlertIngestion.Presentation.Endpoints;
using OpsCopilot.AlertIngestion.Presentation.Extensions;
using OpsCopilot.AlertIngestion.Application.Abstractions;
using OpsCopilot.ApiHost.Dispatch;
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
using OpsCopilot.Packs.Presentation.Endpoints;
using OpsCopilot.Packs.Presentation.Extensions;
using OpsCopilot.Rag.Presentation.Extensions;
using OpsCopilot.Reporting.Presentation.Blazor.Components;
using OpsCopilot.Prompting.Infrastructure.Extensions;
using OpsCopilot.ApiHost.Infrastructure;

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
// Tenancy must precede Governance so ITenantConfigProvider is available when
// TenantAwareGovernanceOptionsResolver is resolved.
builder.Services
    .AddAgentRunsModule(builder.Configuration)
    .AddAlertIngestionModule()
    .AddTenancyModule(builder.Configuration)
    .AddGovernanceModule(builder.Configuration, startupLogger)
    .AddSafeActionsModule(builder.Configuration)
    .AddVectorStoreInfrastructure(builder.Configuration)
    .AddRagModule(builder.Configuration)
    .AddReportingModule(builder.Configuration)
    .AddEvaluationModule()
    .AddConnectorsModule(builder.Configuration)
    .AddPacksModule(builder.Configuration)
    .AddPromptingModule(builder.Configuration);

// Slice 127: Override NullAlertTriageDispatcher with the real orchestrator-backed dispatcher.
// AddAlertIngestionModule registers NullAlertTriageDispatcher; Replace swaps it here at the
// composition root without modifying the module's own DI extensions.
builder.Services.Replace(ServiceDescriptor.Singleton(typeof(IAlertTriageDispatcher), typeof(TriageOrchestratorDispatcher)));

// Slice 130: Register stuck-run watchdog to catch runs left in Running after server restart.
builder.Services.Configure<StuckRunWatchdogOptions>(
    builder.Configuration.GetSection("AgentRun:StuckRunWatchdog"));
builder.Services.AddHostedService<StuckRunWatchdog>();

builder.Services.AddRazorComponents();

// ── Health checks (Slice 140) ─────────────────────────────────────────────────
builder.Services.AddOpsCopilotHealthChecks(builder.Configuration);

// ── Observability ─────────────────────────────────────────────────────────────
builder.Logging.AddConsole();

var app = builder.Build();

// ── Database bootstrap ────────────────────────────────────────────────────────
await app.UseAgentRunsMigrations();
await app.UseSafeActionsMigrations();
await app.UseTenancyMigrations();
await app.UsePromptingMigrations();

// ── Health probes (Slice 140) — /healthz/live, /healthz/ready, /healthz ─────────
app.MapOpsCopilotHealthChecks();

// ── Module endpoints ──────────────────────────────────────────────────────────
app.MapAlertIngestionEndpoints();   // POST /ingest/alert
app.MapAgentRunEndpoints();         // POST /agent/triage
app.MapSessionEndpoints();          // GET  /session/{sessionId}
app.MapFeedbackEndpoints();         // POST /agent/runs/{runId}/feedback
app.MapSafeActionEndpoints();       // /safe-actions/*
app.MapReportingEndpoints();            // /reports/safe-actions/*
app.MapPlatformReportingEndpoints();    // /reports/platform/*
app.MapAgentRunsReportingEndpoints();   // /reports/agent-runs/*
app.MapDashboardEndpoints();            // /reports/dashboard/*
app.MapEvaluationEndpoints();           // /evaluation/*
app.MapTenancyEndpoints();              // /tenants/*
app.MapPlatformPacksEndpoints();        // /reports/platform/packs
app.MapPackRunbookEndpoints();          // /runbooks/{runbookName}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>();          // /app/dashboard (Blazor SSR operator UI)

app.Run();

