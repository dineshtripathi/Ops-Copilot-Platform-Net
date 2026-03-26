using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpsCopilot.Connectors.Infrastructure.Extensions;
using OpsCopilot.Packs.Presentation.Extensions;
using OpsCopilot.WorkerHost.Workers;

// ─────────────────────────────────────────────────────────────────────────────
// OpsCopilot.WorkerHost — background worker host
// Replays dead-lettered SafeAction proposals. No execution capability added.
// ─────────────────────────────────────────────────────────────────────────────

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddConnectorsModule(builder.Configuration);
builder.Services.AddPacksModule(builder.Configuration);
builder.Services.AddHostedService<ProposalDeadLetterReplayWorker>();

var host = builder.Build();
await host.RunAsync();

