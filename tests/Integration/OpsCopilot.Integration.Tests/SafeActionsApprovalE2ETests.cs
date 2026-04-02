using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Application.Orchestration;
using OpsCopilot.SafeActions.Domain;
using OpsCopilot.SafeActions.Domain.Entities;
using OpsCopilot.SafeActions.Domain.Repositories;
using OpsCopilot.SafeActions.Presentation.Contracts;
using OpsCopilot.SafeActions.Presentation.Endpoints;
using OpsCopilot.SafeActions.Presentation.Identity;
using Xunit;

namespace OpsCopilot.Integration.Tests;

/// <summary>
/// E2E tests for SafeActions propose → approve flow with in-memory TestServer.
/// Verifies the full lifecycle: propose → GET (Proposed) → approve → GET (Approved).
/// </summary>
public sealed class SafeActionsApprovalE2ETests
{
    private static async Task<(WebApplication App, HttpClient Client, Mock<IActionRecordRepository> Repo)>
        CreateTestHost()
    {
        ActionRecord? capturedRecord = null;

        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.CreateActionRecordAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tenantId, Guid runId, string actionType,
                string payload, string? rollback, string? guidance, CancellationToken _) =>
            {
                capturedRecord = ActionRecord.Create(tenantId, runId, actionType, payload, rollback, guidance);
                return capturedRecord;
            });

        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => capturedRecord);

        repo.Setup(r => r.SaveAsync(It.IsAny<ActionRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        repo.Setup(r => r.AppendApprovalAsync(It.IsAny<ApprovalRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        repo.Setup(r => r.GetAuditSummariesAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, AuditSummary>());

        repo.Setup(r => r.GetApprovalsForActionAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApprovalRecord>());

        repo.Setup(r => r.GetExecutionLogsForActionAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExecutionLog>());

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SafeActions:AllowActorHeaderFallback"] = "false",
            ["SafeActions:AllowAnonymousActorFallback"] = "false"
        });

        builder.Services.AddAuthentication(E2ETestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, E2ETestAuthHandler>(
                E2ETestAuthHandler.SchemeName, null);
        builder.Services.AddAuthorization();

        builder.Services.AddSingleton(repo.Object);
        builder.Services.AddSingleton(Mock.Of<IActionExecutor>());
        builder.Services.AddSingleton(Mock.Of<ISafeActionPolicy>(p =>
            p.Evaluate(It.IsAny<string>(), It.IsAny<string>()) == PolicyDecision.Allow()));
        builder.Services.AddSingleton(Mock.Of<ITenantExecutionPolicy>());
        builder.Services.AddSingleton(Mock.Of<ISafeActionsTelemetry>());
        builder.Services.AddSingleton(Mock.Of<IExecutionThrottlePolicy>(p =>
            p.Evaluate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()) == ThrottleDecision.Allow()));
        builder.Services.AddSingleton(Mock.Of<IActionTypeCatalog>(c =>
            c.IsAllowlisted(It.IsAny<string>()) == true));
        builder.Services.AddSingleton(Mock.Of<IGovernancePolicyClient>(g =>
            g.EvaluateToolAllowlist(It.IsAny<string>(), It.IsAny<string>()) == PolicyDecision.Allow() &&
            g.EvaluateTokenBudget(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<int?>()) == BudgetDecision.Allow(8192)));
        builder.Services.AddSingleton<SafeActionOrchestrator>();
        builder.Services.AddSingleton<IActorIdentityResolver, ClaimsActorIdentityResolver>();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapSafeActionEndpoints();
        await app.StartAsync();

        return (app, app.GetTestClient(), repo);
    }

    [Fact]
    public async Task Propose_get_approve_get_full_lifecycle()
    {
        var (app, client, repo) = await CreateTestHost();
        try
        {
            var runId = Guid.NewGuid();

            // ── Step 1: Propose ──────────────────────────────────────
            var proposeMsg = new HttpRequestMessage(HttpMethod.Post, "/safe-actions")
            {
                Content = JsonContent.Create(new ProposeActionRequest
                {
                    RunId = runId,
                    ActionType = "restart_service",
                    ProposedPayloadJson = """{"service":"web-app-01","region":"eastus"}""",
                    RollbackPayloadJson = """{"service":"web-app-01","action":"stop"}""",
                    ManualRollbackGuidance = "SSH into host and restart manually"
                })
            };
            proposeMsg.Headers.Add("x-tenant-id", "tenant-e2e-002");

            var proposeResponse = await client.SendAsync(proposeMsg);
            Assert.Equal(HttpStatusCode.Created, proposeResponse.StatusCode);

            using var proposeDoc = JsonDocument.Parse(await proposeResponse.Content.ReadAsStringAsync());
            var actionRecordId = proposeDoc.RootElement.GetProperty("actionRecordId").GetGuid();
            Assert.NotEqual(Guid.Empty, actionRecordId);
            Assert.Contains($"/safe-actions/{actionRecordId}", proposeResponse.Headers.Location?.ToString());

            // ── Step 2: GET (verify status = Proposed) ───────────────
            var get1 = new HttpRequestMessage(HttpMethod.Get, $"/safe-actions/{actionRecordId}");
            get1.Headers.Add("x-tenant-id", "tenant-e2e-002");
            var getResponse1 = await client.SendAsync(get1);
            Assert.Equal(HttpStatusCode.OK, getResponse1.StatusCode);

            using var getDoc1 = JsonDocument.Parse(await getResponse1.Content.ReadAsStringAsync());
            Assert.Equal("Proposed", getDoc1.RootElement.GetProperty("status").GetString());

            // ── Step 3: Approve ──────────────────────────────────────
            var approveMsg = new HttpRequestMessage(HttpMethod.Post, $"/safe-actions/{actionRecordId}/approve")
            {
                Content = JsonContent.Create(new ApproveActionRequest { Reason = "Approved for deployment" })
            };
            approveMsg.Headers.Add("x-tenant-id", "tenant-e2e-002");
            var approveResponse = await client.SendAsync(approveMsg);
            Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

            // ── Step 4: GET (verify status = Approved) ───────────────
            var get2 = new HttpRequestMessage(HttpMethod.Get, $"/safe-actions/{actionRecordId}");
            get2.Headers.Add("x-tenant-id", "tenant-e2e-002");
            var getResponse2 = await client.SendAsync(get2);
            Assert.Equal(HttpStatusCode.OK, getResponse2.StatusCode);

            using var getDoc2 = JsonDocument.Parse(await getResponse2.Content.ReadAsStringAsync());
            Assert.Equal("Approved", getDoc2.RootElement.GetProperty("status").GetString());

            // ── Verify repository interactions ───────────────────────
            repo.Verify(r => r.CreateActionRecordAsync(
                "tenant-e2e-002", runId, "restart_service",
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()), Times.Once);
            repo.Verify(r => r.AppendApprovalAsync(
                It.IsAny<ApprovalRecord>(), It.IsAny<CancellationToken>()), Times.Once);
            repo.Verify(r => r.SaveAsync(
                It.IsAny<ActionRecord>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
