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
using OpsCopilot.SafeActions.Presentation.Endpoints;
using OpsCopilot.BuildingBlocks.Contracts.Governance;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// HTTP-level tests proving the tenant execution policy gate returns
/// 400 Bad Request with reason code when the tenant is denied.
/// </summary>
public class SafeActionTenantExecutionPolicyEndpointTests
{
    // ── Helper: create a record in the required state ───────────

    private static ActionRecord CreateApprovedRecord()
    {
        var record = ActionRecord.Create(
            tenantId: "t-blocked",
            runId: Guid.NewGuid(),
            actionType: "restart_pod",
            proposedPayloadJson: "{\"target\":\"pod-1\"}",
            rollbackPayloadJson: "{\"undo\":\"stop_pod\"}");
        record.Approve();
        return record;
    }

    private static ActionRecord CreateRollbackApprovedRecord()
    {
        var record = CreateApprovedRecord();
        record.MarkExecuting();
        record.CompleteExecution("{\"target\":\"pod-1\"}", "{\"ok\":true}");
        record.RequestRollback();
        record.ApproveRollback();
        return record;
    }

    // ── Helper: spin up a test host with tenant policy deny ─────

    private static async Task<(WebApplication App, HttpClient Client)> CreateTestHost(
        ActionRecord record,
        PolicyDecision tenantDecision)
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        // Enable the execution guard so requests pass through to the orchestrator.
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["SafeActions:EnableExecution"] = "true"
            });

        // Repository returns the provided record.
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        builder.Services.AddSingleton(repo.Object);

        // Executor — never reached when policy denies.
        builder.Services.AddSingleton(Mock.Of<IActionExecutor>());

        // Proposal-time policy — not relevant for execute paths.
        builder.Services.AddSingleton(Mock.Of<ISafeActionPolicy>());

        // Tenant execution policy — returns the provided decision.
        var tenantPolicy = new Mock<ITenantExecutionPolicy>(MockBehavior.Strict);
        tenantPolicy.Setup(p => p.EvaluateExecution(record.TenantId, record.ActionType))
                    .Returns(tenantDecision);
        builder.Services.AddSingleton(tenantPolicy.Object);
        builder.Services.AddSingleton(Mock.Of<ISafeActionsTelemetry>());
        builder.Services.AddSingleton(Mock.Of<IExecutionThrottlePolicy>(p =>
            p.Evaluate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()) == ThrottleDecision.Allow()));

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

    // ── Execute endpoint → 400 on tenant deny ──────────────────

    [Fact]
    public async Task Execute_Returns400_WithReasonCode_WhenTenantDenied()
    {
        var record = CreateApprovedRecord();
        var deny = PolicyDecision.Deny(
            "tenant_not_authorized_for_action",
            "Tenant t-blocked is not authorized to execute restart_pod");

        var (app, client) = await CreateTestHost(record, deny);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(body);
            Assert.Equal("tenant_not_authorized_for_action",
                json.RootElement.GetProperty("reasonCode").GetString());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Rollback-execute endpoint → 400 on tenant deny ──────────

    [Fact]
    public async Task RollbackExecute_Returns400_WithReasonCode_WhenTenantDenied()
    {
        var record = CreateRollbackApprovedRecord();
        var deny = PolicyDecision.Deny(
            "tenant_not_authorized_for_action",
            "Tenant t-blocked is not authorized to execute restart_pod");

        var (app, client) = await CreateTestHost(record, deny);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/rollback/execute", null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(body);
            Assert.Equal("tenant_not_authorized_for_action",
                json.RootElement.GetProperty("reasonCode").GetString());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Execute endpoint → 200 when tenant allowed ──────────────

    [Fact]
    public async Task Execute_Returns200_WhenTenantAllowed()
    {
        var record = CreateApprovedRecord();

        // Tenant policy allows — executor also needs to be set up.
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        repo.Setup(r => r.SaveAsync(record, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AppendExecutionLogAsync(It.IsAny<ExecutionLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var executor = new Mock<IActionExecutor>(MockBehavior.Strict);
        executor.Setup(e => e.ExecuteAsync(
                record.ActionType, record.ProposedPayloadJson, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActionExecutionResult(true, "{\"ok\":true}", 0));

        var tenantPolicy = new Mock<ITenantExecutionPolicy>(MockBehavior.Strict);
        tenantPolicy.Setup(p => p.EvaluateExecution("t-blocked", "restart_pod"))
                    .Returns(PolicyDecision.Allow());

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["SafeActions:EnableExecution"] = "true"
            });

        builder.Services.AddSingleton(repo.Object);
        builder.Services.AddSingleton(executor.Object);
        builder.Services.AddSingleton(Mock.Of<ISafeActionPolicy>());
        builder.Services.AddSingleton(tenantPolicy.Object);
        builder.Services.AddSingleton(Mock.Of<ISafeActionsTelemetry>());
        builder.Services.AddSingleton(Mock.Of<IExecutionThrottlePolicy>(p =>
            p.Evaluate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()) == ThrottleDecision.Allow()));
        builder.Services.AddSingleton<SafeActionOrchestrator>();

        var app = builder.Build();
        app.MapSafeActionEndpoints();
        await app.StartAsync();
        var client = app.GetTestClient();

        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    // ── Precedence: 501 guard wins over tenant deny ─────────────

    [Fact]
    public async Task Execute_Returns501_EvenWhenTenantDenied_IfExecutionDisabled()
    {
        var record = CreateApprovedRecord();

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        // Execution disabled — guard fires first, tenant policy never reached.
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["SafeActions:EnableExecution"] = "false"
            });

        builder.Services.AddSingleton(Mock.Of<IActionRecordRepository>());
        builder.Services.AddSingleton(Mock.Of<IActionExecutor>());
        builder.Services.AddSingleton(Mock.Of<ISafeActionPolicy>());
        builder.Services.AddSingleton(Mock.Of<ITenantExecutionPolicy>());
        builder.Services.AddSingleton(Mock.Of<ISafeActionsTelemetry>());
        builder.Services.AddSingleton(Mock.Of<IExecutionThrottlePolicy>(p =>
            p.Evaluate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()) == ThrottleDecision.Allow()));
        builder.Services.AddSingleton<SafeActionOrchestrator>();

        var app = builder.Build();
        app.MapSafeActionEndpoints();
        await app.StartAsync();
        var client = app.GetTestClient();

        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    // ── Execute Replay → 409 Conflict (Slice 14) ────────────────

    [Fact]
    public async Task Execute_Returns409_WhenAlreadyCompleted()
    {
        var record = CreateApprovedRecord();
        record.MarkExecuting();
        record.CompleteExecution("{\"target\":\"pod-1\"}", "{\"ok\":true}");

        var (app, client) = await CreateTestHost(record, PolicyDecision.Allow());
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

    [Fact]
    public async Task Execute_Returns409_WhenAlreadyFailed()
    {
        var record = CreateApprovedRecord();
        record.MarkExecuting();
        record.FailExecution("{\"target\":\"pod-1\"}", "{\"error\":\"boom\"}");

        var (app, client) = await CreateTestHost(record, PolicyDecision.Allow());
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

    // ── Rollback Replay → 409 Conflict (Slice 14) ───────────────

    [Fact]
    public async Task RollbackExecute_Returns409_WhenAlreadyRolledBack()
    {
        var record = CreateRollbackApprovedRecord();
        record.CompleteRollback("{\"rolled_back\":true}");

        var (app, client) = await CreateTestHost(record, PolicyDecision.Allow());
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/rollback/execute", null);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    [Fact]
    public async Task RollbackExecute_Returns409_WhenRollbackFailed()
    {
        var record = CreateRollbackApprovedRecord();
        record.FailRollback("{\"error\":\"timeout\"}");

        var (app, client) = await CreateTestHost(record, PolicyDecision.Allow());
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/rollback/execute", null);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
        finally
        {
            await DisposeHost(app);
        }
    }
}
