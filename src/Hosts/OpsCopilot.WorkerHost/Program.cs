using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpsCopilot.AlertIngestion.Presentation.Extensions;
using OpsCopilot.BuildingBlocks.Infrastructure.Configuration;
using OpsCopilot.Connectors.Infrastructure.Extensions;
using OpsCopilot.Packs.Presentation.Extensions;
using OpsCopilot.WorkerHost.Workers;

// ─────────────────────────────────────────────────────────────────────────────
// OpsCopilot.WorkerHost — background worker host
// Replays dead-lettered SafeAction proposals and polls for incoming alerts.
// ─────────────────────────────────────────────────────────────────────────────

var builder = Host.CreateApplicationBuilder(args);

using var bootstrapLoggerFactory = LoggerFactory.Create(lb =>
    lb.AddConsole().SetMinimumLevel(LogLevel.Information));
var startupLogger = bootstrapLoggerFactory.CreateLogger("Startup");
startupLogger.LogInformation("[Startup] Environment: {Env}", builder.Environment.EnvironmentName);
builder.Configuration.AddOpsCopilotKeyVault(
    builder.Configuration["KeyVault:VaultUri"],
    startupLogger);

builder.Services.AddConnectorsModule(builder.Configuration);
builder.Services.AddPacksModule(builder.Configuration);
builder.Services.AddAlertIngestionModule();

// Alert ingestion source — NullAlertIngestionSource by default.
// Replace with a real queue implementation (e.g. Azure Service Bus) at composition root.
builder.Services.AddSingleton<IAlertIngestionSource, NullAlertIngestionSource>();

builder.Services.AddHostedService<ProposalDeadLetterReplayWorker>();
builder.Services.AddHostedService<AlertIngestionWorker>();

// Tenant digest source — NullTenantDigestSource by default.
// Replace with a real implementation backed by ITenantRegistry + IAgentRunsReportingQueryService
// at the composition root when those modules are wired into WorkerHost.
builder.Services.AddSingleton<ITenantDigestSource, NullTenantDigestSource>();
builder.Services.AddHostedService<DigestWorker>();

var host = builder.Build();
await host.RunAsync();

