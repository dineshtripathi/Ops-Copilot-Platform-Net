using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpsCopilot.Packs.Infrastructure.Extensions;
using OpsCopilot.WorkerHost.Workers;

// ─────────────────────────────────────────────────────────────────────────────
// OpsCopilot.WorkerHost — background worker host
// Replays dead-lettered SafeAction proposals. No execution capability added.
// ─────────────────────────────────────────────────────────────────────────────

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddPacksInfrastructure(builder.Configuration);
builder.Services.AddHostedService<ProposalDeadLetterReplayWorker>();

var host = builder.Build();
await host.RunAsync();

