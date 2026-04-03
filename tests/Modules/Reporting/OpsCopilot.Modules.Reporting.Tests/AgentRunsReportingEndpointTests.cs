using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;
using OpsCopilot.Reporting.Presentation.Endpoints;

namespace OpsCopilot.Modules.Reporting.Tests;

/// <summary>
/// HTTP-level tests for Slice 61: Agent Runs Reporting — dashboard foundations.
/// </summary>
public class AgentRunsReportingEndpointTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ── Test host factory ───────────────────────────────────────

    private static async Task<(WebApplication App, HttpClient Client, Mock<IAgentRunsReportingQueryService> Svc)>
        CreateTestHost()
    {
        var svc = new Mock<IAgentRunsReportingQueryService>(MockBehavior.Strict);

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton(svc.Object);

        var app = builder.Build();
        app.MapAgentRunsReportingEndpoints();
        await app.StartAsync();

        return (app, app.GetTestClient(), svc);
    }

    private static async Task DisposeHost(WebApplication app)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static HttpRequestMessage Get(string path, string? tenantId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrEmpty(tenantId))
            request.Headers.Add("x-tenant-id", tenantId);
        return request;
    }

    private static AgentRunsSummaryReport EmptySummary()
        => new(0, 0, 0, 0, 0, 0, null, null, null, 0.0, null, null);

    // ═══════════════════════════════════════════════════════════════
    // Summary — /reports/agent-runs/summary
    // ═══════════════════════════════════════════════════════════════

    // ── 1. Summary — 200 OK with data ───────────────────────────

    [Fact]
    public async Task Summary_ReturnsOk_WithPopulatedData()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var expected = new AgentRunsSummaryReport(
                TotalRuns: 10, Completed: 7, Failed: 2, Degraded: 1,
                Pending: 0, Running: 0,
                AvgDurationMs: 1500.5, AvgTotalTokens: 850.0,
                TotalEstimatedCost: 0.42m, CitationCoverageRate: 0.8,
                FromUtc: null, ToUtc: null);

            svc.Setup(s => s.GetSummaryAsync(null, null, "tenant-test", It.IsAny<CancellationToken>()))
               .ReturnsAsync(expected);

            var response = await client.SendAsync(
                Get("/reports/agent-runs/summary", tenantId: "tenant-test"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<AgentRunsSummaryReport>(body, JsonOpts);
            Assert.NotNull(result);
            Assert.Equal(10, result.TotalRuns);
            Assert.Equal(7, result.Completed);
            Assert.Equal(2, result.Failed);
        }
        finally { await DisposeHost(app); }
    }

    // ── 2. Summary — x-tenant-id is propagated to service ──────

    [Fact]
    public async Task Summary_PropagatesTenantId_ToService()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            svc.Setup(s => s.GetSummaryAsync(null, null, "tenant-abc", It.IsAny<CancellationToken>()))
               .ReturnsAsync(EmptySummary());

            var response = await client.SendAsync(
                Get("/reports/agent-runs/summary", tenantId: "tenant-abc"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            svc.Verify(s => s.GetSummaryAsync(null, null, "tenant-abc", It.IsAny<CancellationToken>()), Times.Once);
        }
        finally { await DisposeHost(app); }
    }

    // ── 3. Summary — invalid fromUtc returns 400 ────────────────

    [Fact]
    public async Task Summary_InvalidFromUtc_Returns400()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var response = await client.SendAsync(
                Get("/reports/agent-runs/summary?fromUtc=not-a-date", tenantId: "tenant-test"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally { await DisposeHost(app); }
    }

    // ── 4. Summary — from > to returns 400 ──────────────────────

    [Fact]
    public async Task Summary_FromAfterTo_Returns400()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var response = await client.SendAsync(
                Get("/reports/agent-runs/summary?fromUtc=2025-12-01&toUtc=2025-01-01", tenantId: "tenant-test"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally { await DisposeHost(app); }
    }

    // ── 5. Summary — zeroed report returns 200 ──────────────────

    [Fact]
    public async Task Summary_AllZeros_ReturnsOk()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            svc.Setup(s => s.GetSummaryAsync(null, null, "tenant-test", It.IsAny<CancellationToken>()))
               .ReturnsAsync(EmptySummary());

            var response = await client.SendAsync(
                Get("/reports/agent-runs/summary", tenantId: "tenant-test"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<AgentRunsSummaryReport>(body, JsonOpts);
            Assert.NotNull(result);
            Assert.Equal(0, result.TotalRuns);
        }
        finally { await DisposeHost(app); }
    }

    // ═══════════════════════════════════════════════════════════════
    // Trend — /reports/agent-runs/trend
    // ═══════════════════════════════════════════════════════════════

    // ── 6. Trend — 200 OK with data ─────────────────────────────

    [Fact]
    public async Task Trend_ReturnsOk_WithData()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var points = new List<AgentRunsTrendPoint>
            {
                new(new DateOnly(2025, 6, 1), TotalRuns: 5, CompletedRuns: 4, FailedRuns: 1, DegradedRuns: 0),
                new(new DateOnly(2025, 6, 2), TotalRuns: 3, CompletedRuns: 3, FailedRuns: 0, DegradedRuns: 0)
            };

            svc.Setup(s => s.GetTrendAsync(null, null, "tenant-test", It.IsAny<CancellationToken>()))
               .ReturnsAsync(points);

            var response = await client.SendAsync(
                Get("/reports/agent-runs/trend", tenantId: "tenant-test"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<List<AgentRunsTrendPoint>>(body, JsonOpts);
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal(5, result[0].TotalRuns);
        }
        finally { await DisposeHost(app); }
    }

    // ── 7. Trend — x-tenant-id is propagated to service ─────────

    [Fact]
    public async Task Trend_PropagatesTenantId_ToService()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            svc.Setup(s => s.GetTrendAsync(null, null, "tenant-xyz", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new List<AgentRunsTrendPoint>());

            var response = await client.SendAsync(
                Get("/reports/agent-runs/trend", tenantId: "tenant-xyz"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            svc.Verify(s => s.GetTrendAsync(null, null, "tenant-xyz", It.IsAny<CancellationToken>()), Times.Once);
        }
        finally { await DisposeHost(app); }
    }

    // ═══════════════════════════════════════════════════════════════
    // Tool Usage — /reports/agent-runs/tool-usage
    // ═══════════════════════════════════════════════════════════════

    // ── 8. ToolUsage — 200 OK with rows ─────────────────────────

    [Fact]
    public async Task ToolUsage_ReturnsOk_WithRows()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var rows = new List<ToolUsageSummaryRow>
            {
                new("azure-monitor-query", TotalCalls: 20, SuccessfulCalls: 18, FailedCalls: 2, AvgDurationMs: 340.5),
                new("kubectl-get-pods",    TotalCalls: 8,  SuccessfulCalls: 8,  FailedCalls: 0, AvgDurationMs: 210.0)
            };

            svc.Setup(s => s.GetToolUsageAsync(null, null, "tenant-test", It.IsAny<CancellationToken>()))
               .ReturnsAsync(rows);

            var response = await client.SendAsync(
                Get("/reports/agent-runs/tool-usage", tenantId: "tenant-test"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<List<ToolUsageSummaryRow>>(body, JsonOpts);
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal("azure-monitor-query", result[0].ToolName);
            Assert.Equal(20, result[0].TotalCalls);
        }
        finally { await DisposeHost(app); }
    }

    // ── 9. ToolUsage — x-tenant-id is propagated to service ─────

    [Fact]
    public async Task ToolUsage_PropagatesTenantId_ToService()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            svc.Setup(s => s.GetToolUsageAsync(null, null, "tenant-t1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new List<ToolUsageSummaryRow>());

            var response = await client.SendAsync(
                Get("/reports/agent-runs/tool-usage", tenantId: "tenant-t1"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            svc.Verify(s => s.GetToolUsageAsync(null, null, "tenant-t1", It.IsAny<CancellationToken>()), Times.Once);
        }
        finally { await DisposeHost(app); }
    }

    // ═══════════════════════════════════════════════════════════════
    // Tenant isolation — missing x-tenant-id must return 400
    // ═══════════════════════════════════════════════════════════════

    // ── 10. Summary — missing tenant returns 400 ────────────────

    [Fact]
    public async Task Summary_MissingTenantId_Returns400()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            // No x-tenant-id header — service must never be reached.
            var response = await client.SendAsync(Get("/reports/agent-runs/summary"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            svc.VerifyNoOtherCalls();
        }
        finally { await DisposeHost(app); }
    }

    // ── 11. Trend — missing tenant returns 400 ───────────────────

    [Fact]
    public async Task Trend_MissingTenantId_Returns400()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var response = await client.SendAsync(Get("/reports/agent-runs/trend"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            svc.VerifyNoOtherCalls();
        }
        finally { await DisposeHost(app); }
    }

    // ── 12. ToolUsage — missing tenant returns 400 ───────────────

    [Fact]
    public async Task ToolUsage_MissingTenantId_Returns400()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var response = await client.SendAsync(Get("/reports/agent-runs/tool-usage"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            svc.VerifyNoOtherCalls();
        }
        finally { await DisposeHost(app); }
    }
}
