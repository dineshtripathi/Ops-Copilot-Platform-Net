using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpsCopilot.AgentRuns.Application;
using OpsCopilot.AgentRuns.Infrastructure.Persistence;
using OpsCopilot.AgentRuns.Infrastructure.Tooling;
using OpsCopilot.AgentRuns.Presentation;
using OpsCopilot.AlertIngestion.Presentation;
using OpsCopilot.BuildingBlocks.Contracts.Tools;
using OpsCopilot.McpHost.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSingleton<IAgentRunRepository, InMemoryAgentRunRepository>();
builder.Services.AddSingleton<IToolExecutor, ToolExecutor>();
builder.Services.AddSingleton<TriageService>();
builder.Services.AddSingleton<IKqlQueryTool, FakeKqlQueryTool>();

var app = builder.Build();

app.MapGet("/health", () => Results.NoContent());
app.MapAlertIngestionEndpoints();
app.MapAgentRunEndpoints();

app.Run();