using System.Net;
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
using OpsCopilot.SafeActions.Domain;
using OpsCopilot.SafeActions.Domain.Entities;
using OpsCopilot.SafeActions.Domain.Enums;
using OpsCopilot.SafeActions.Domain.Repositories;
using OpsCopilot.SafeActions.Presentation.Endpoints;
using OpsCopilot.BuildingBlocks.Contracts.Governance;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// HTTP-level tests for Slice 15: SafeActions Execution Audit Querying.
/// Covers filter parameters, validation, tenant isolation, and audit summary enrichment.
/// </summary>
public class SafeActionQueryEndpointTests
{
    // ── Helpers ──────────────────────────────────────────────────

    private static ActionRecord CreateRecord(
        string tenantId = "t-query",
        string actionType = "restart_pod")
    {
        return ActionRecord.Create(
            tenantId,
            Guid.NewGuid(),
            actionType,
            "{\"target\":\"pod-1\"}",
            "{\"undo\":\"stop_pod\"}");
    }

    private static async Task<(WebApplication App, HttpClient Client, Mock<IActionRecordRepository> Repo)>
        CreateTestHost()
    {
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton(repo.Object);
        builder.Services.AddSingleton(Mock.Of<IActionExecutor>());
        builder.Services.AddSingleton(Mock.Of<ISafeActionPolicy>());
        builder.Services.AddSingleton(Mock.Of<ITenantExecutionPolicy>());
        builder.Services.AddSingleton<SafeActionOrchestrator>();

        var app = builder.Build();
        app.MapSafeActionEndpoints();
        await app.StartAsync();

        return (app, app.GetTestClient(), repo);
    }

    private static async Task DisposeHost(WebApplication app)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    private static HttpRequestMessage CreateListRequest(
        string query = "",
        string tenantId = "t-query")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/safe-actions{query}");
        request.Headers.Add("x-tenant-id", tenantId);
        return request;
    }

    // ── AC-7: Tenant isolation (missing header → 400) ───────────

    [Fact]
    public async Task List_Returns400_WhenTenantHeaderMissing()
    {
        var (app, client, _) = await CreateTestHost();
        try
        {
            var response = await client.GetAsync("/safe-actions");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-8: Invalid status enum → 400 ────────────────────────

    [Fact]
    public async Task List_Returns400_WhenStatusInvalid()
    {
        var (app, client, _) = await CreateTestHost();
        try
        {
            var response = await client.SendAsync(
                CreateListRequest("?status=NotARealStatus"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Invalid status value", body);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-8: Invalid rollbackStatus enum → 400 ────────────────

    [Fact]
    public async Task List_Returns400_WhenRollbackStatusInvalid()
    {
        var (app, client, _) = await CreateTestHost();
        try
        {
            var response = await client.SendAsync(
                CreateListRequest("?rollbackStatus=BOGUS"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Invalid rollbackStatus value", body);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-8: Invalid fromUtc date → 400 ───────────────────────

    [Fact]
    public async Task List_Returns400_WhenFromUtcInvalid()
    {
        var (app, client, _) = await CreateTestHost();
        try
        {
            var response = await client.SendAsync(
                CreateListRequest("?fromUtc=not-a-date"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Invalid fromUtc value", body);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-8: Invalid toUtc date → 400 ─────────────────────────

    [Fact]
    public async Task List_Returns400_WhenToUtcInvalid()
    {
        var (app, client, _) = await CreateTestHost();
        try
        {
            var response = await client.SendAsync(
                CreateListRequest("?toUtc=yesterday"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Invalid toUtc value", body);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-8: fromUtc > toUtc → 400 ────────────────────────────

    [Fact]
    public async Task List_Returns400_WhenFromUtcAfterToUtc()
    {
        var (app, client, _) = await CreateTestHost();
        try
        {
            var response = await client.SendAsync(
                CreateListRequest("?fromUtc=2025-12-31T00:00:00Z&toUtc=2025-01-01T00:00:00Z"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("fromUtc must not be after toUtc", body);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-2: Filter by status ──────────────────────────────────

    [Fact]
    public async Task List_FilterByStatus_CallsQueryByTenant()
    {
        var record = CreateRecord();
        var records = new List<ActionRecord> { record };

        var (app, client, repo) = await CreateTestHost();
        try
        {
            repo.Setup(r => r.QueryByTenantAsync(
                    "t-query", ActionStatus.Proposed, null, null, null, null, null,
                    50, It.IsAny<CancellationToken>()))
                .ReturnsAsync(records);
            repo.Setup(r => r.GetAuditSummariesAsync(
                    It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, AuditSummary>());

            var response = await client.SendAsync(
                CreateListRequest("?status=Proposed"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            repo.Verify(r => r.QueryByTenantAsync(
                "t-query", ActionStatus.Proposed, null, null, null, null, null,
                50, It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-3: Filter by rollbackStatus ──────────────────────────

    [Fact]
    public async Task List_FilterByRollbackStatus_CallsQueryByTenant()
    {
        var record = CreateRecord();
        var records = new List<ActionRecord> { record };

        var (app, client, repo) = await CreateTestHost();
        try
        {
            repo.Setup(r => r.QueryByTenantAsync(
                    "t-query", null, RollbackStatus.Available, null, null, null, null,
                    50, It.IsAny<CancellationToken>()))
                .ReturnsAsync(records);
            repo.Setup(r => r.GetAuditSummariesAsync(
                    It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, AuditSummary>());

            var response = await client.SendAsync(
                CreateListRequest("?rollbackStatus=Available"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            repo.Verify(r => r.QueryByTenantAsync(
                "t-query", null, RollbackStatus.Available, null, null, null, null,
                50, It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-1: Filter by actionType ──────────────────────────────

    [Fact]
    public async Task List_FilterByActionType_CallsQueryByTenant()
    {
        var record = CreateRecord();
        var records = new List<ActionRecord> { record };

        var (app, client, repo) = await CreateTestHost();
        try
        {
            repo.Setup(r => r.QueryByTenantAsync(
                    "t-query", null, null, "restart_pod", null, null, null,
                    50, It.IsAny<CancellationToken>()))
                .ReturnsAsync(records);
            repo.Setup(r => r.GetAuditSummariesAsync(
                    It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, AuditSummary>());

            var response = await client.SendAsync(
                CreateListRequest("?actionType=restart_pod"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            repo.Verify(r => r.QueryByTenantAsync(
                "t-query", null, null, "restart_pod", null, null, null,
                50, It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-4: Filter by hasExecutionLogs ────────────────────────

    [Fact]
    public async Task List_FilterByHasExecutionLogs_CallsQueryByTenant()
    {
        var records = new List<ActionRecord>();

        var (app, client, repo) = await CreateTestHost();
        try
        {
            repo.Setup(r => r.QueryByTenantAsync(
                    "t-query", null, null, null, true, null, null,
                    50, It.IsAny<CancellationToken>()))
                .ReturnsAsync(records);
            repo.Setup(r => r.GetAuditSummariesAsync(
                    It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, AuditSummary>());

            var response = await client.SendAsync(
                CreateListRequest("?hasExecutionLogs=true"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            repo.Verify(r => r.QueryByTenantAsync(
                "t-query", null, null, null, true, null, null,
                50, It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-5: Filter by date range ──────────────────────────────

    [Fact]
    public async Task List_FilterByDateRange_CallsQueryByTenant()
    {
        var records = new List<ActionRecord>();

        var (app, client, repo) = await CreateTestHost();
        try
        {
            repo.Setup(r => r.QueryByTenantAsync(
                    "t-query", null, null, null, null,
                    It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                    50, It.IsAny<CancellationToken>()))
                .ReturnsAsync(records);
            repo.Setup(r => r.GetAuditSummariesAsync(
                    It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, AuditSummary>());

            var response = await client.SendAsync(
                CreateListRequest("?fromUtc=2025-01-01T00:00:00Z&toUtc=2025-06-01T00:00:00Z"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            repo.Verify(r => r.QueryByTenantAsync(
                "t-query", null, null, null, null,
                It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                50, It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-6: Combined filters ──────────────────────────────────

    [Fact]
    public async Task List_CombinedFilters_CallsQueryByTenant()
    {
        var records = new List<ActionRecord> { CreateRecord() };

        var (app, client, repo) = await CreateTestHost();
        try
        {
            repo.Setup(r => r.QueryByTenantAsync(
                    "t-query", ActionStatus.Completed, RollbackStatus.None,
                    "restart_pod", null, It.IsAny<DateTimeOffset?>(), null,
                    25, It.IsAny<CancellationToken>()))
                .ReturnsAsync(records);
            repo.Setup(r => r.GetAuditSummariesAsync(
                    It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, AuditSummary>());

            var response = await client.SendAsync(
                CreateListRequest("?status=Completed&rollbackStatus=None&actionType=restart_pod&limit=25&fromUtc=2025-01-01T00:00:00Z"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            repo.Verify(r => r.QueryByTenantAsync(
                "t-query", ActionStatus.Completed, RollbackStatus.None,
                "restart_pod", null, It.IsAny<DateTimeOffset?>(), null,
                25, It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-9: Audit summary fields in response ──────────────────

    [Fact]
    public async Task List_Response_ContainsAuditSummaryFields()
    {
        var record = CreateRecord();
        var records = new List<ActionRecord> { record };
        var auditTime = DateTimeOffset.Parse("2025-06-01T12:00:00Z");
        var audit = new AuditSummary(3, auditTime, true, 2, "Approved", auditTime);

        var (app, client, repo) = await CreateTestHost();
        try
        {
            repo.Setup(r => r.QueryByTenantAsync(
                    "t-query", ActionStatus.Proposed, null, null, null, null, null,
                    50, It.IsAny<CancellationToken>()))
                .ReturnsAsync(records);
            repo.Setup(r => r.GetAuditSummariesAsync(
                    It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, AuditSummary>
                {
                    [record.ActionRecordId] = audit,
                });

            var response = await client.SendAsync(
                CreateListRequest("?status=Proposed"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(body);
            var first = json.RootElement.EnumerateArray().First();

            Assert.Equal(3, first.GetProperty("executionLogCount").GetInt32());
            Assert.True(first.GetProperty("lastExecutionSuccess").GetBoolean());
            Assert.Equal(2, first.GetProperty("approvalCount").GetInt32());
            Assert.Equal("Approved", first.GetProperty("lastApprovalDecision").GetString());
            Assert.True(first.TryGetProperty("lastExecutionAtUtc", out _));
            Assert.True(first.TryGetProperty("lastApprovalAtUtc", out _));
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-9: Default audit fields when no audit data ───────────

    [Fact]
    public async Task List_Response_ContainsEmptyAuditWhenNoData()
    {
        var record = CreateRecord();
        var records = new List<ActionRecord> { record };

        var (app, client, repo) = await CreateTestHost();
        try
        {
            repo.Setup(r => r.QueryByTenantAsync(
                    "t-query", ActionStatus.Proposed, null, null, null, null, null,
                    50, It.IsAny<CancellationToken>()))
                .ReturnsAsync(records);
            repo.Setup(r => r.GetAuditSummariesAsync(
                    It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, AuditSummary>());

            var response = await client.SendAsync(
                CreateListRequest("?status=Proposed"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(body);
            var first = json.RootElement.EnumerateArray().First();

            Assert.Equal(0, first.GetProperty("executionLogCount").GetInt32());
            Assert.Equal(JsonValueKind.Null, first.GetProperty("lastExecutionAtUtc").ValueKind);
            Assert.Equal(JsonValueKind.Null, first.GetProperty("lastExecutionSuccess").ValueKind);
            Assert.Equal(0, first.GetProperty("approvalCount").GetInt32());
            Assert.Equal(JsonValueKind.Null, first.GetProperty("lastApprovalDecision").ValueKind);
            Assert.Equal(JsonValueKind.Null, first.GetProperty("lastApprovalAtUtc").ValueKind);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── RunId-only path still works (no filters) ────────────────

    [Fact]
    public async Task List_RunIdOnly_CallsListByRunAsync()
    {
        var runId = Guid.NewGuid();
        var record = CreateRecord();
        var records = new List<ActionRecord> { record };

        var (app, client, repo) = await CreateTestHost();
        try
        {
            repo.Setup(r => r.GetByRunIdAsync(runId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(records);
            repo.Setup(r => r.GetAuditSummariesAsync(
                    It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, AuditSummary>());

            var response = await client.SendAsync(
                CreateListRequest($"?runId={runId}"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            repo.Verify(r => r.GetByRunIdAsync(runId, It.IsAny<CancellationToken>()), Times.Once);
            repo.Verify(r => r.QueryByTenantAsync(
                It.IsAny<string>(), It.IsAny<ActionStatus?>(), It.IsAny<RollbackStatus?>(),
                It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<DateTimeOffset?>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Default path (no runId, no filters) → QueryByTenant ─────

    [Fact]
    public async Task List_NoParams_CallsQueryByTenant()
    {
        var records = new List<ActionRecord>();

        var (app, client, repo) = await CreateTestHost();
        try
        {
            repo.Setup(r => r.QueryByTenantAsync(
                    "t-query", null, null, null, null, null, null,
                    50, It.IsAny<CancellationToken>()))
                .ReturnsAsync(records);
            repo.Setup(r => r.GetAuditSummariesAsync(
                    It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, AuditSummary>());

            var response = await client.SendAsync(
                CreateListRequest());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            repo.Verify(r => r.QueryByTenantAsync(
                "t-query", null, null, null, null, null, null,
                50, It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-2: Case-insensitive enum parsing ─────────────────────

    [Fact]
    public async Task List_StatusParsing_IsCaseInsensitive()
    {
        var records = new List<ActionRecord>();

        var (app, client, repo) = await CreateTestHost();
        try
        {
            repo.Setup(r => r.QueryByTenantAsync(
                    "t-query", ActionStatus.Completed, null, null, null, null, null,
                    50, It.IsAny<CancellationToken>()))
                .ReturnsAsync(records);
            repo.Setup(r => r.GetAuditSummariesAsync(
                    It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, AuditSummary>());

            var response = await client.SendAsync(
                CreateListRequest("?status=completed"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Limit clamping ──────────────────────────────────────────

    [Fact]
    public async Task List_LimitClamped_To_MaxListLimit()
    {
        var records = new List<ActionRecord>();

        var (app, client, repo) = await CreateTestHost();
        try
        {
            repo.Setup(r => r.QueryByTenantAsync(
                    "t-query", null, null, null, null, null, null,
                    200, It.IsAny<CancellationToken>()))
                .ReturnsAsync(records);
            repo.Setup(r => r.GetAuditSummariesAsync(
                    It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, AuditSummary>());

            var response = await client.SendAsync(
                CreateListRequest("?limit=999"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            repo.Verify(r => r.QueryByTenantAsync(
                "t-query", null, null, null, null, null, null,
                200, It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            await DisposeHost(app);
        }
    }
}
