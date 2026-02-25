using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Application.Orchestration;
using OpsCopilot.SafeActions.Domain.Repositories;
using OpsCopilot.SafeActions.Presentation.Endpoints;
using OpsCopilot.BuildingBlocks.Contracts.Governance;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// HTTP-level tests for the SafeActions execution guard.
/// The guard returns 501 Not Implemented when SafeActions:EnableExecution is false (or absent).
/// </summary>
public class SafeActionExecutionGuardTests
{
    // ── Helper: spin up a minimal test host with the guard config ────

    private static async Task<(WebApplication App, HttpClient Client)> CreateTestHost(
        bool? enableExecution)
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();

        // Suppress default host logging noise during tests.
        builder.Logging.ClearProviders();

        if (enableExecution.HasValue)
        {
            builder.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["SafeActions:EnableExecution"] = enableExecution.Value.ToString()
                });
        }
        // When enableExecution is null, the key is absent — guard defaults to false.

        // Register stub dependencies for SafeActionOrchestrator (never invoked behind a 501 guard).
        builder.Services.AddSingleton(Mock.Of<IActionRecordRepository>());
        builder.Services.AddSingleton(Mock.Of<IActionExecutor>());
        builder.Services.AddSingleton(Mock.Of<ISafeActionPolicy>());
        builder.Services.AddSingleton(Mock.Of<ITenantExecutionPolicy>(p =>
            p.EvaluateExecution(It.IsAny<string>(), It.IsAny<string>()) == PolicyDecision.Allow()));
        builder.Services.AddSingleton<SafeActionOrchestrator>();

        var app = builder.Build();
        app.MapSafeActionEndpoints();
        await app.StartAsync();

        return (app, app.GetTestClient());
    }

    private static async Task DisposeHost(WebApplication app)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    // ── Execute endpoint guard ──────────────────────────────────────

    [Fact]
    public async Task Execute_Returns501_WhenFlagIsFalse()
    {
        var (app, client) = await CreateTestHost(enableExecution: false);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{Guid.NewGuid()}/execute", null);

            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    [Fact]
    public async Task Execute_Returns501_WhenFlagIsAbsent()
    {
        var (app, client) = await CreateTestHost(enableExecution: null);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{Guid.NewGuid()}/execute", null);

            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Rollback-execute endpoint guard ─────────────────────────────

    [Fact]
    public async Task RollbackExecute_Returns501_WhenFlagIsFalse()
    {
        var (app, client) = await CreateTestHost(enableExecution: false);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{Guid.NewGuid()}/rollback/execute", null);

            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    [Fact]
    public async Task RollbackExecute_Returns501_WhenFlagIsAbsent()
    {
        var (app, client) = await CreateTestHost(enableExecution: null);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{Guid.NewGuid()}/rollback/execute", null);

            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        }
        finally
        {
            await DisposeHost(app);
        }
    }
}
