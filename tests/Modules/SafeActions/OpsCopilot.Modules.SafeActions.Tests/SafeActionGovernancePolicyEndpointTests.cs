using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Application.Orchestration;
using OpsCopilot.SafeActions.Domain.Entities;
using OpsCopilot.SafeActions.Domain.Repositories;
using OpsCopilot.SafeActions.Presentation.Endpoints;
using OpsCopilot.BuildingBlocks.Contracts.Governance;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// HTTP-level tests proving the governance-backed safe-action policy gate
/// returns 400 Bad Request with frozen reason code when the tenant is denied,
/// and 201 Created when the tenant is allowed.
/// </summary>
public class SafeActionGovernancePolicyEndpointTests
{
    private static readonly Guid TestRunId = Guid.NewGuid();

    // ── Helpers ─────────────────────────────────────────────────

    private static string BuildProposeBody(string actionType = "restart_pod") =>
        JsonSerializer.Serialize(new
        {
            runId = TestRunId,
            actionType,
            proposedPayloadJson = "{\"target\":\"pod-1\"}"
        });

    private static HttpRequestMessage CreateProposeRequest(
        string tenantId, string actionType = "restart_pod")
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "/safe-actions")
        {
            Content = new StringContent(
                BuildProposeBody(actionType), Encoding.UTF8, "application/json")
        };
        msg.Headers.Add("x-tenant-id", tenantId);
        return msg;
    }

    private static async Task<(WebApplication App, HttpClient Client)> CreateTestHost(
        PolicyDecision policyDecision,
        ActionRecord? repoResult = null)
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        // ISafeActionPolicy — the gate under test.
        var policy = new Mock<ISafeActionPolicy>(MockBehavior.Strict);
        policy.Setup(p => p.Evaluate(It.IsAny<string>(), It.IsAny<string>()))
              .Returns(policyDecision);
        builder.Services.AddSingleton(policy.Object);

        // Repository — only reached when all gates pass.
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        if (repoResult is not null)
        {
            repo.Setup(r => r.CreateActionRecordAsync(
                    It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(repoResult);
        }
        builder.Services.AddSingleton(repo.Object);

        builder.Services.AddSingleton(Mock.Of<IActionExecutor>());
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

        var app = builder.Build();
        app.MapSafeActionEndpoints();
        await app.StartAsync();

        return (app, app.GetTestClient());
    }

    private static async Task<(WebApplication App, HttpClient Client)> CreateTenantIsolationHost(
        Mock<ISafeActionPolicy> policy,
        ActionRecord repoResult)
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton(policy.Object);

        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.CreateActionRecordAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(repoResult);
        builder.Services.AddSingleton(repo.Object);

        builder.Services.AddSingleton(Mock.Of<IActionExecutor>());
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

    // ── Propose → 400 when governance-backed policy denies ──────

    [Fact]
    public async Task Propose_Returns400_WithGovernanceToolDenied_WhenPolicyDenies()
    {
        var deny = PolicyDecision.Deny(
            "governance_tool_denied",
            "Denied by governance tool allowlist (policyReason=tool_not_in_allowlist): restart_pod not permitted");

        var (app, client) = await CreateTestHost(deny);
        try
        {
            var response = await client.SendAsync(
                CreateProposeRequest("t-blocked"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            Assert.Equal("governance_tool_denied",
                json.RootElement.GetProperty("reasonCode").GetString());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Propose → 400 message contains policyReason= ────────────

    [Fact]
    public async Task Propose_Returns400_WithPolicyReasonInMessage_WhenPolicyDenies()
    {
        var deny = PolicyDecision.Deny(
            "governance_tool_denied",
            "Denied by governance tool allowlist (policyReason=action_disabled_by_admin): restart_pod blocked");

        var (app, client) = await CreateTestHost(deny);
        try
        {
            var response = await client.SendAsync(
                CreateProposeRequest("t-blocked"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            var message = json.RootElement.GetProperty("message").GetString();
            Assert.Contains("policyReason=action_disabled_by_admin", message);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Propose → 201 when policy allows ────────────────────────

    [Fact]
    public async Task Propose_Returns201_WhenPolicyAllows()
    {
        var record = ActionRecord.Create(
            "t-allowed", TestRunId, "restart_pod",
            "{\"target\":\"pod-1\"}", null, null);

        var (app, client) = await CreateTestHost(PolicyDecision.Allow(), record);
        try
        {
            var response = await client.SendAsync(
                CreateProposeRequest("t-allowed"));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Tenant isolation: allowed and denied tenants ─────────────

    [Fact]
    public async Task Propose_TenantIsolation_AllowedAndDeniedTenantsGetCorrectResponses()
    {
        var allowRecord = ActionRecord.Create(
            "t-allowed", TestRunId, "restart_pod",
            "{\"target\":\"pod-1\"}", null, null);

        var policy = new Mock<ISafeActionPolicy>(MockBehavior.Strict);
        policy.Setup(p => p.Evaluate("t-allowed", "restart_pod"))
              .Returns(PolicyDecision.Allow());
        policy.Setup(p => p.Evaluate("t-blocked", "restart_pod"))
              .Returns(PolicyDecision.Deny(
                  "governance_tool_denied",
                  "Denied by governance tool allowlist (policyReason=tool_not_in_allowlist): restart_pod not permitted"));

        var (app, client) = await CreateTenantIsolationHost(policy, allowRecord);
        try
        {
            // Allowed tenant → 201
            var allowResponse = await client.SendAsync(
                CreateProposeRequest("t-allowed"));
            Assert.Equal(HttpStatusCode.Created, allowResponse.StatusCode);

            // Blocked tenant → 400 with governance_tool_denied
            var denyResponse = await client.SendAsync(
                CreateProposeRequest("t-blocked"));
            Assert.Equal(HttpStatusCode.BadRequest, denyResponse.StatusCode);

            var body = await denyResponse.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            Assert.Equal("governance_tool_denied",
                json.RootElement.GetProperty("reasonCode").GetString());
        }
        finally
        {
            await DisposeHost(app);
        }
    }
}
