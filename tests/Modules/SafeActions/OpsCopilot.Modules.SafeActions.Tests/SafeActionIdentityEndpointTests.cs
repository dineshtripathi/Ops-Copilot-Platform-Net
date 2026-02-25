using System.Net;
using System.Net.Http.Json;
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
using OpsCopilot.SafeActions.Presentation.Contracts;
using OpsCopilot.SafeActions.Presentation.Endpoints;
using OpsCopilot.SafeActions.Presentation.Identity;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// HTTP-level integration tests for the actor identity resolver integration
/// in approve, reject, and rollback/approve endpoints.
/// Validates AC-8 (401 when no identity), AC-6 (header fallback), and AC-10 (claim propagation).
/// </summary>
public class SafeActionIdentityEndpointTests
{
    // ── Host helpers ────────────────────────────────────────────────

    private static async Task<(WebApplication App, HttpClient Client)> CreateHost(
        IActionRecordRepository repository,
        bool allowHeaderFallback = false,
        bool allowAnonymousFallback = false)
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["SafeActions:AllowActorHeaderFallback"] = allowHeaderFallback.ToString(),
                ["SafeActions:AllowAnonymousActorFallback"] = allowAnonymousFallback.ToString()
            });

        builder.Services.AddSingleton(repository);
        builder.Services.AddSingleton(Mock.Of<ISafeActionPolicy>());
        builder.Services.AddSingleton(Mock.Of<IActionExecutor>());
        builder.Services.AddSingleton(Mock.Of<ITenantExecutionPolicy>(p =>
            p.EvaluateExecution(It.IsAny<string>(), It.IsAny<string>()) == PolicyDecision.Allow()));
        builder.Services.AddSingleton(Mock.Of<ISafeActionsTelemetry>());
        builder.Services.AddSingleton<SafeActionOrchestrator>();
        builder.Services.AddSingleton<IActorIdentityResolver, ClaimsActorIdentityResolver>();

        var app = builder.Build();
        app.MapSafeActionEndpoints();
        await app.StartAsync();

        return (app, app.GetTestClient());
    }

    private static async Task<(WebApplication App, HttpClient Client)> CreateHostWithMockResolver(
        IActionRecordRepository repository,
        ActorIdentityResult? resolvedIdentity)
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        var mockResolver = Mock.Of<IActorIdentityResolver>(
            r => r.Resolve(It.IsAny<Microsoft.AspNetCore.Http.HttpContext>()) == resolvedIdentity);

        builder.Services.AddSingleton(repository);
        builder.Services.AddSingleton(Mock.Of<ISafeActionPolicy>());
        builder.Services.AddSingleton(Mock.Of<IActionExecutor>());
        builder.Services.AddSingleton(Mock.Of<ITenantExecutionPolicy>(p =>
            p.EvaluateExecution(It.IsAny<string>(), It.IsAny<string>()) == PolicyDecision.Allow()));
        builder.Services.AddSingleton(Mock.Of<ISafeActionsTelemetry>());
        builder.Services.AddSingleton<SafeActionOrchestrator>();
        builder.Services.AddSingleton<IActorIdentityResolver>(mockResolver);

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

    private static ActionRecord CreateProposedRecord()
    {
        return ActionRecord.Create(
            "t-identity", Guid.NewGuid(), "restart_pod",
            "{\"target\":\"pod-1\"}", "{\"undo\":\"stop\"}");
    }

    private static Mock<IActionRecordRepository> RepositoryReturning(ActionRecord record)
    {
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        repo.Setup(r => r.AppendApprovalAsync(It.IsAny<ApprovalRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveAsync(record, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return repo;
    }

    // ── 401 when resolver returns null (AC-8) ───────────────────────

    [Fact]
    public async Task Approve_Returns401_WhenResolverReturnsNull()
    {
        var record = CreateProposedRecord();
        var repo = RepositoryReturning(record);
        var (app, client) = await CreateHostWithMockResolver(repo.Object, null);
        try
        {
            var response = await client.PostAsJsonAsync(
                $"/safe-actions/{record.ActionRecordId}/approve",
                new { Reason = "lgtm" });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    [Fact]
    public async Task Reject_Returns401_WhenResolverReturnsNull()
    {
        var record = CreateProposedRecord();
        var repo = RepositoryReturning(record);
        var (app, client) = await CreateHostWithMockResolver(repo.Object, null);
        try
        {
            var response = await client.PostAsJsonAsync(
                $"/safe-actions/{record.ActionRecordId}/reject",
                new { Reason = "not needed" });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    [Fact]
    public async Task RollbackApprove_Returns401_WhenResolverReturnsNull()
    {
        var record = CreateProposedRecord();
        record.Approve();
        record.MarkExecuting();
        record.CompleteExecution("{}", "{\"ok\":true}");
        record.RequestRollback();

        var repo = RepositoryReturning(record);
        var (app, client) = await CreateHostWithMockResolver(repo.Object, null);
        try
        {
            var response = await client.PostAsJsonAsync(
                $"/safe-actions/{record.ActionRecordId}/rollback/approve",
                new { Reason = "rollback needed" });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── 401 with real resolver, all fallbacks disabled ──────────────

    [Fact]
    public async Task Approve_Returns401_WithRealResolver_WhenFallbacksDisabled()
    {
        var record = CreateProposedRecord();
        var repo = RepositoryReturning(record);
        var (app, client) = await CreateHost(repo.Object,
            allowHeaderFallback: false,
            allowAnonymousFallback: false);
        try
        {
            // No claims, no header → no identity
            var response = await client.PostAsJsonAsync(
                $"/safe-actions/{record.ActionRecordId}/approve",
                new { Reason = "lgtm" });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── 200 with header fallback enabled (AC-6) ─────────────────────

    [Fact]
    public async Task Approve_Returns200_WithHeaderFallback()
    {
        var record = CreateProposedRecord();
        var repo = RepositoryReturning(record);
        var (app, client) = await CreateHost(repo.Object,
            allowHeaderFallback: true);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post,
                $"/safe-actions/{record.ActionRecordId}/approve")
            {
                Content = JsonContent.Create(new { Reason = "approved via header" })
            };
            request.Headers.Add("x-actor-id", "header-user");

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal("Approved", root.GetProperty("status").GetString());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    [Fact]
    public async Task Reject_Returns200_WithHeaderFallback()
    {
        var record = CreateProposedRecord();
        var repo = RepositoryReturning(record);
        var (app, client) = await CreateHost(repo.Object,
            allowHeaderFallback: true);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post,
                $"/safe-actions/{record.ActionRecordId}/reject")
            {
                Content = JsonContent.Create(new { Reason = "rejected via header" })
            };
            request.Headers.Add("x-actor-id", "header-user");

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal("Rejected", root.GetProperty("status").GetString());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── ActorId propagation with mock resolver (AC-10) ──────────────

    [Fact]
    public async Task Approve_PassesActorIdFromResolver_ToOrchestrator()
    {
        var record = CreateProposedRecord();
        var repo = RepositoryReturning(record);
        ApprovalRecord? capturedApproval = null;
        repo.Setup(r => r.AppendApprovalAsync(It.IsAny<ApprovalRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ApprovalRecord, CancellationToken>((a, _) => capturedApproval = a)
            .Returns(Task.CompletedTask);
        var identity = new ActorIdentityResult("claims-user-42", "claim", true);
        var (app, client) = await CreateHostWithMockResolver(repo.Object, identity);
        try
        {
            var response = await client.PostAsJsonAsync(
                $"/safe-actions/{record.ActionRecordId}/approve",
                new { Reason = "auto-approved" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal("Approved", root.GetProperty("status").GetString());
            Assert.NotNull(capturedApproval);
            Assert.Equal("claims-user-42", capturedApproval!.ApproverIdentity);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    [Fact]
    public async Task Reject_PassesActorIdFromResolver_ToOrchestrator()
    {
        var record = CreateProposedRecord();
        var repo = RepositoryReturning(record);
        ApprovalRecord? capturedApproval = null;
        repo.Setup(r => r.AppendApprovalAsync(It.IsAny<ApprovalRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ApprovalRecord, CancellationToken>((a, _) => capturedApproval = a)
            .Returns(Task.CompletedTask);
        var identity = new ActorIdentityResult("claims-user-99", "claim", true);
        var (app, client) = await CreateHostWithMockResolver(repo.Object, identity);
        try
        {
            var response = await client.PostAsJsonAsync(
                $"/safe-actions/{record.ActionRecordId}/reject",
                new { Reason = "denied" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal("Rejected", root.GetProperty("status").GetString());
            Assert.NotNull(capturedApproval);
            Assert.Equal("claims-user-99", capturedApproval!.ApproverIdentity);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Anonymous fallback (AC-7) ───────────────────────────────────

    [Fact]
    public async Task Approve_Returns200_WithAnonymousFallback()
    {
        var record = CreateProposedRecord();
        var repo = RepositoryReturning(record);
        ApprovalRecord? capturedApproval = null;
        repo.Setup(r => r.AppendApprovalAsync(It.IsAny<ApprovalRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ApprovalRecord, CancellationToken>((a, _) => capturedApproval = a)
            .Returns(Task.CompletedTask);
        var (app, client) = await CreateHost(repo.Object,
            allowAnonymousFallback: true);
        try
        {
            // No claims, no header → anonymous fallback → "unknown"
            var response = await client.PostAsJsonAsync(
                $"/safe-actions/{record.ActionRecordId}/approve",
                new { Reason = "anonymous approve" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal("Approved", root.GetProperty("status").GetString());
            Assert.NotNull(capturedApproval);
            Assert.Equal("unknown", capturedApproval!.ApproverIdentity);
        }
        finally
        {
            await DisposeHost(app);
        }
    }
}
