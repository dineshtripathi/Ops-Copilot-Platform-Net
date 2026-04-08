using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Agents.Hosting.AspNetCore;
using OpsCopilot.AgentRuns.Application.Orchestration;
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
using OpsCopilot.Rag.Presentation.Endpoints;
using OpsCopilot.Reporting.Presentation.Blazor.Components;
using OpsCopilot.Evaluation.Infrastructure.Extensions;
using OpsCopilot.Prompting.Infrastructure.Extensions;
using OpsCopilot.ApiHost.Infrastructure;
using Microsoft.AspNetCore.Authentication;     // Slice 202: SignOutAsync, AuthenticationProperties
using Microsoft.AspNetCore.Authentication.Cookies;  // Slice 202: CookieAuthenticationDefaults
using Microsoft.AspNetCore.Authentication.OpenIdConnect; // Slice 202: OpenIdConnectDefaults


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
    .AddEvaluationLlmGraded()
    .AddEvaluationInfrastructure(builder.Configuration)
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

// Slice 202: IHttpContextAccessor — lets Blazor SSR pages read OIDC tokens from
// the cookie session and derive the base URL from the current request.
builder.Services.AddHttpContextAccessor();

// ── Authentication & authorisation (Slice 149) ───────────────────────────────
// Registers Entra ID JWT bearer (or DevBypass handler in Development).
// All endpoints require auth via fallback policy; health probes use .AllowAnonymous().
builder.Services.AddOpsCopilotAuthentication(builder.Configuration);

// ── Rate limiting (Slice 151) ─────────────────────────────────────────────────
// GlobalLimiter: triage-tier for /agent/triage* and /ingest/alert* (LLM + MCP);
// default-tier for all other API endpoints; /healthz* exempt.
// Partition key = authenticated NameIdentifier claim (set by Slice 149).
builder.Services.AddOpsCopilotRateLimiting(builder.Configuration);

// ── Observability ─────────────────────────────────────────────────────────────
builder.Services.AddOpsCopilotOpenTelemetry(builder.Configuration);
builder.Logging.AddConsole();

// Slice 147: MAF bootstrap — registers CloudAdapter + IAgent → TriageAgentActivityHandler
builder.AddAgent<TriageAgentActivityHandler>();

// ── Error handling & ProblemDetails (Slice 163) ───────────────────────────────
// Registers IProblemDetailsService so unhandled exceptions return RFC 7807 JSON.
// In Development, exception type and message are surfaced in the response body.
builder.Services.AddOpsCopilotErrorHandling(builder.Environment.IsDevelopment());

// ── DI container validation (Slice 163) ───────────────────────────────────────
// Validates all DI registrations at host-build time (not at first use) in
// non-Production environments, surfacing missing dependencies before any request
// is served.  Scope validation is limited to Development where it adds the most
// value without risking false positives from optional service configurations.
builder.Host.UseDefaultServiceProvider((ctx, opts) =>
{
    opts.ValidateOnBuild = !ctx.HostingEnvironment.IsProduction();
    opts.ValidateScopes  = ctx.HostingEnvironment.IsDevelopment();
});

// ── Graceful shutdown (Slice 163) ─────────────────────────────────────────────
// Allows 30 s for in-flight requests to drain before the container is terminated.
// Azure Container Apps sends SIGTERM then waits up to the revision\'s grace period.
builder.Services.Configure<HostOptions>(o =>
    o.ShutdownTimeout = TimeSpan.FromSeconds(30));

// ── Request body size limit (Slice 163) ───────────────────────────────────────
// Triage payloads are a few KB at most.  A 1 MB cap prevents body-bomb DoS
// against the LLM + MCP call chain without impacting normal usage.
builder.WebHost.ConfigureKestrel(kestrel =>
    kestrel.Limits.MaxRequestBodySize = 1 * 1024 * 1024);

var app = builder.Build();

// ── Exception handler (Slice 163) — outermost middleware; catches all unhandled ──
// exceptions and returns 500 ProblemDetails JSON instead of bare HTTP 500 / HTML.
// Requires AddProblemDetails() registered above.  Must be the first middleware.
app.UseExceptionHandler();

// ── Status code pages (Slice 163) — 401/403/404 → ProblemDetails JSON ─────────
// Without this, bare 401/403 responses have no body; clients cannot distinguish
// an authentication failure from a missing resource.
app.UseStatusCodePages();

// ── Security response headers (Slice 163) ─────────────────────────────────────
// Applied to every response.  TLS/HSTS is enforced at the Container Apps ingress
// level; these headers provide defence-in-depth for the application layer.
app.Use((context, next) =>
{
    var h = context.Response.Headers;
    h["X-Content-Type-Options"]              = "nosniff";
    h["X-Frame-Options"]                     = "DENY";
    h["Referrer-Policy"]                     = "strict-origin-when-cross-origin";
    h["X-Permitted-Cross-Domain-Policies"]   = "none";
    h["Permissions-Policy"] =
        "camera=(), microphone=(), geolocation=(), payment=()";
    return next(context);
});

// ── Auth middleware (Slice 149) — must precede endpoint mapping ─────────────────
app.UseAuthentication();
app.UseAuthorization();

// ── Rate limiting (Slice 151) — after auth so claims are available ──────────────
app.UseRateLimiter();

// ── Database bootstrap ────────────────────────────────────────────────────────
app.Logger.LogInformation("[Startup] Running EF Core migrations...");
try
{
    await app.UseAgentRunsMigrations();
    app.Logger.LogInformation("[Startup] AgentRuns migrations OK");
    await app.UseSafeActionsMigrations();
    app.Logger.LogInformation("[Startup] SafeActions migrations OK");
    await app.UseTenancyMigrations();
    app.Logger.LogInformation("[Startup] Tenancy migrations OK");
    await app.UsePromptingMigrations();
    app.Logger.LogInformation("[Startup] Prompting migrations OK");
    await app.UseEvaluationMigrations();
    app.Logger.LogInformation("[Startup] Evaluation migrations OK — all migrations complete");
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex,
        "[Startup] MIGRATION FAILED — {ExceptionType}: {Message}",
        ex.GetType().Name, ex.Message);
    throw;
}

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
app.MapRagAdminEndpoints();             // POST /rag/runbooks/reindex

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>();          // /app/dashboard (Blazor SSR operator UI)

// ── Slice 202: Login / Logout / AccessDenied routes ───────────────────────────
// These are .AllowAnonymous() so the auth fallback policy doesn't intercept them.
app.MapGet("/account/login", (HttpContext ctx, string? returnUrl) =>
{
    var props = new AuthenticationProperties
    {
        RedirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/app/dashboard" : returnUrl
    };
    return Results.Challenge(props, [OpenIdConnectDefaults.AuthenticationScheme]);
}).AllowAnonymous();

app.MapGet("/account/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    var props = new AuthenticationProperties { RedirectUri = "/account/login" };
    return Results.SignOut(props, [OpenIdConnectDefaults.AuthenticationScheme]);
}).AllowAnonymous();

app.MapGet("/account/accessdenied", () => Results.Content("""
    <!DOCTYPE html><html><head><title>Access Denied — Ops Copilot</title>
    <meta name='viewport' content='width=device-width,initial-scale=1'/>
    <style>body{font-family:sans-serif;display:flex;align-items:center;justify-content:center;
    height:100vh;margin:0;background:#0f172a;color:#e2e8f0}
    .card{background:#1e293b;padding:2rem 3rem;border-radius:1rem;text-align:center}
    a{color:#60a5fa;text-decoration:none}a:hover{text-decoration:underline}</style></head>
    <body><div class='card'><h1>Access Denied</h1>
    <p>You don&rsquo;t have permission to view this page.</p>
    <p><a href='/account/login'>Sign in with a different account</a>&ensp;&bull;&ensp;
    <a href='/app/dashboard'>Go to Dashboard</a></p></div></body></html>
    """, "text/html"))
    .AllowAnonymous();

// Slice 147: MAF activity endpoint — POST /api/agent/messages
// Slice 149: requireAuth flipped to true; Entra token required in production.
app.MapAgentEndpoints(requireAuth: true, path: "/api/agent/messages");

app.Run();

