using System.Net;
using System.Text.Json;
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
using OpsCopilot.SafeActions.Domain.Entities;
using OpsCopilot.SafeActions.Domain.Enums;
using OpsCopilot.SafeActions.Domain.Repositories;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.SafeActions.Infrastructure.Executors;
using OpsCopilot.SafeActions.Presentation.Endpoints;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// HTTP-level integration tests for the dry-run execution pipeline.
/// Feature flag EnableExecution=true, real DryRunActionExecutor, mock repository.
/// </summary>
public class SafeActionDryRunEndpointTests
{
    // ── Helper: test host with flag=true and real DryRunActionExecutor ──

    private static async Task<(WebApplication App, HttpClient Client)> CreateDryRunHost(
        IActionRecordRepository repository)
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["SafeActions:EnableExecution"] = "True"
            });

        builder.Services.AddSingleton(repository);
        builder.Services.AddSingleton<IActionExecutor, DryRunActionExecutor>();
        builder.Services.AddSingleton(Mock.Of<ISafeActionPolicy>());
        builder.Services.AddSingleton(Mock.Of<ITenantExecutionPolicy>(p =>
            p.EvaluateExecution(It.IsAny<string>(), It.IsAny<string>()) == PolicyDecision.Allow()));
        builder.Services.AddSingleton(Mock.Of<ISafeActionsTelemetry>());
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

    private static ActionRecord CreateApprovedRecord(
        string payloadJson = "{\"target\":\"pod-1\"}",
        string? rollbackPayloadJson = "{\"undo\":\"stop_pod\"}")
    {
        var record = ActionRecord.Create(
            "t-dry-run", Guid.NewGuid(), "restart_pod",
            payloadJson, rollbackPayloadJson);
        record.Approve();
        return record;
    }

    private static Mock<IActionRecordRepository> CreateRepoMock(ActionRecord record)
    {
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        repo.Setup(r => r.SaveAsync(record, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AppendExecutionLogAsync(
                It.IsAny<ExecutionLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return repo;
    }

    // ── Execute endpoint with flag=true ─────────────────────────────

    [Fact]
    public async Task Execute_Returns200_WithDryRunResponse_WhenFlagIsTrue()
    {
        var record = CreateApprovedRecord();
        var repo = CreateRepoMock(record);
        var (app, client) = await CreateDryRunHost(repo.Object);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("Completed", root.GetProperty("status").GetString());
            var outcomeJson = root.GetProperty("outcomeJson").GetString();
            Assert.NotNull(outcomeJson);

            using var outcome = JsonDocument.Parse(outcomeJson!);
            var oRoot = outcome.RootElement;
            Assert.Equal("dry-run", oRoot.GetProperty("mode").GetString());
            Assert.Equal("restart_pod", oRoot.GetProperty("actionType").GetString());
            Assert.Equal("success", oRoot.GetProperty("simulatedOutcome").GetString());
            Assert.Equal("dry-run completed", oRoot.GetProperty("reason").GetString());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    [Fact]
    public async Task Execute_Returns200_WithFailedStatus_WhenSimulateFailurePayload()
    {
        var record = CreateApprovedRecord(
            payloadJson: "{\"target\":\"pod-1\",\"simulateFailure\":true}");
        var repo = CreateRepoMock(record);
        var (app, client) = await CreateDryRunHost(repo.Object);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("Failed", root.GetProperty("status").GetString());
            var outcomeJson = root.GetProperty("outcomeJson").GetString();
            Assert.Contains("simulated_failure", outcomeJson);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    [Fact]
    public async Task Execute_Returns404_WhenRecordNotFound()
    {
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActionRecord?)null);

        var (app, client) = await CreateDryRunHost(repo.Object);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{Guid.NewGuid()}/execute", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    [Fact]
    public async Task Execute_Returns409_WhenRecordNotInApprovedState()
    {
        // Proposed (not approved) → MarkExecuting throws InvalidOperationException → 409
        var record = ActionRecord.Create(
            "t-dry-run", Guid.NewGuid(), "restart_pod", "{\"target\":\"pod-1\"}");
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var (app, client) = await CreateDryRunHost(repo.Object);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Rollback/execute endpoint with flag=true ────────────────────

    [Fact]
    public async Task RollbackExecute_Returns200_WithDryRunRollbackResponse()
    {
        var record = CreateApprovedRecord();
        record.MarkExecuting();
        record.CompleteExecution("{\"target\":\"pod-1\"}", "{\"ok\":true}");
        record.RequestRollback();
        record.ApproveRollback();

        var repo = CreateRepoMock(record);
        var (app, client) = await CreateDryRunHost(repo.Object);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/rollback/execute", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("RolledBack", root.GetProperty("rollbackStatus").GetString());
            var rollbackOutcomeJson = root.GetProperty("rollbackOutcomeJson").GetString();
            Assert.NotNull(rollbackOutcomeJson);

            using var outcome = JsonDocument.Parse(rollbackOutcomeJson!);
            var oRoot = outcome.RootElement;
            Assert.Equal("dry-run-rollback", oRoot.GetProperty("mode").GetString());
            Assert.Equal("restart_pod", oRoot.GetProperty("actionType").GetString());
            Assert.Equal("success", oRoot.GetProperty("simulatedOutcome").GetString());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    [Fact]
    public async Task RollbackExecute_Returns404_WhenRecordNotFound()
    {
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActionRecord?)null);

        var (app, client) = await CreateDryRunHost(repo.Object);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{Guid.NewGuid()}/rollback/execute", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await DisposeHost(app);
        }
    }
}
