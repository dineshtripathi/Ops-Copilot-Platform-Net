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
/// HTTP-level tests for Slice 62: Dashboard API — GET /reports/dashboard/overview.
/// The endpoint is tested through a real TestHost; IDashboardQueryService is mocked.
/// </summary>
public class DashboardEndpointTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ── Test host factory ──────────────────────────────────────────

    private static async Task<(WebApplication App, HttpClient Client, Mock<IDashboardQueryService> Svc)>
        CreateTestHost()
    {
        var svc = new Mock<IDashboardQueryService>(MockBehavior.Strict);

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton(svc.Object);

        var app = builder.Build();
        app.MapDashboardEndpoints();
        await app.StartAsync();

        return (app, app.GetTestClient(), svc);
    }

    private static async Task DisposeHost(WebApplication app)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    // ── Helpers ────────────────────────────────────────────────────

    private static HttpRequestMessage Get(string path, string? tenantId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrEmpty(tenantId))
            request.Headers.Add("x-tenant-id", tenantId);
        return request;
    }

    private static DashboardOverviewResponse EmptyOverview() =>
        new(
            Summary:    new AgentRunsSummaryReport(0, 0, 0, 0, 0, 0, null, null, null, 0.0, null, null),
            Trend:      Array.Empty<AgentRunsTrendPoint>(),
            TopTools:   Array.Empty<ToolUsageSummaryRow>(),
            RecentRuns: Array.Empty<RecentRunSummary>());

    // ═══════════════════════════════════════════════════════════════
    // Tenant-guard tests
    // ═══════════════════════════════════════════════════════════════

    // ── 1. Missing x-tenant-id → 400 ──────────────────────────────

    [Fact]
    public async Task Overview_MissingTenantId_Returns400()
    {
        var (app, client, _) = await CreateTestHost();
        try
        {
            var response = await client.SendAsync(
                Get("/reports/dashboard/overview"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally { await DisposeHost(app); }
    }

    // ── 2. Missing x-tenant-id → ProblemDetails body ──────────────

    [Fact]
    public async Task Overview_MissingTenantId_ReturnsProblemDetails()
    {
        var (app, client, _) = await CreateTestHost();
        try
        {
            var response = await client.SendAsync(
                Get("/reports/dashboard/overview"));

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("x-tenant-id", body, StringComparison.OrdinalIgnoreCase);
        }
        finally { await DisposeHost(app); }
    }

    // ═══════════════════════════════════════════════════════════════
    // Happy-path tests
    // ═══════════════════════════════════════════════════════════════

    // ── 3. Happy path — 200 OK ─────────────────────────────────────

    [Fact]
    public async Task Overview_ValidRequest_Returns200()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            svc.Setup(s => s.GetOverviewAsync(null, null, "tenant-a", null, null, (int?)null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(EmptyOverview());

            var response = await client.SendAsync(
                Get("/reports/dashboard/overview", tenantId: "tenant-a"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally { await DisposeHost(app); }
    }

    // ── 4. Tenant ID is forwarded to the service ───────────────────

    [Fact]
    public async Task Overview_PropagatesTenantId_ToService()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            svc.Setup(s => s.GetOverviewAsync(null, null, "tenant-xyz", null, null, (int?)null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(EmptyOverview());

            var response = await client.SendAsync(
                Get("/reports/dashboard/overview", tenantId: "tenant-xyz"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            svc.Verify(
                s => s.GetOverviewAsync(null, null, "tenant-xyz", null, null, (int?)null, It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally { await DisposeHost(app); }
    }

    // ── 5. Empty data returns 200 with zero/empty sections ─────────

    [Fact]
    public async Task Overview_EmptyData_Returns200WithZeroSummaryAndEmptyLists()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            svc.Setup(s => s.GetOverviewAsync(null, null, "tenant-a", null, null, (int?)null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(EmptyOverview());

            var response = await client.SendAsync(
                Get("/reports/dashboard/overview", tenantId: "tenant-a"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            Assert.Equal(0, doc.RootElement.GetProperty("summary").GetProperty("totalRuns").GetInt32());
            Assert.Equal(0, doc.RootElement.GetProperty("trend").GetArrayLength());
            Assert.Equal(0, doc.RootElement.GetProperty("topTools").GetArrayLength());
        }
        finally { await DisposeHost(app); }
    }

    // ── 6. Summary fields are mapped correctly ─────────────────────

    [Fact]
    public async Task Overview_SummaryFields_MappedCorrectly()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var summary = new AgentRunsSummaryReport(
                TotalRuns: 20, Completed: 15, Failed: 3, Degraded: 2,
                Pending: 0, Running: 0,
                AvgDurationMs: 1200.0, AvgTotalTokens: 900.0,
                TotalEstimatedCost: 0.75m, CitationCoverageRate: 0.9,
                FromUtc: null, ToUtc: null);

            svc.Setup(s => s.GetOverviewAsync(null, null, "tenant-b", null, null, (int?)null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DashboardOverviewResponse(
                   summary,
                   Array.Empty<AgentRunsTrendPoint>(),
                   Array.Empty<ToolUsageSummaryRow>(),
                   Array.Empty<RecentRunSummary>()));

            var response = await client.SendAsync(
                Get("/reports/dashboard/overview", tenantId: "tenant-b"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var s = doc.RootElement.GetProperty("summary");

            Assert.Equal(20, s.GetProperty("totalRuns").GetInt32());
            Assert.Equal(15, s.GetProperty("completed").GetInt32());
            Assert.Equal(3,  s.GetProperty("failed").GetInt32());
            Assert.Equal(2,  s.GetProperty("degraded").GetInt32());
        }
        finally { await DisposeHost(app); }
    }

    // ── 7. Trend data is forwarded from service ────────────────────

    [Fact]
    public async Task Overview_TrendData_ForwardedFromService()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var trend = new[]
            {
                new AgentRunsTrendPoint(new DateOnly(2025, 1, 1), 5, 4, 1, 0),
                new AgentRunsTrendPoint(new DateOnly(2025, 1, 2), 8, 7, 1, 0),
            };

            svc.Setup(s => s.GetOverviewAsync(null, null, "tenant-c", null, null, (int?)null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DashboardOverviewResponse(
                   new AgentRunsSummaryReport(0, 0, 0, 0, 0, 0, null, null, null, 0.0, null, null),
                   trend,
                   Array.Empty<ToolUsageSummaryRow>(),
                   Array.Empty<RecentRunSummary>()));

            var response = await client.SendAsync(
                Get("/reports/dashboard/overview", tenantId: "tenant-c"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            Assert.Equal(2, doc.RootElement.GetProperty("trend").GetArrayLength());
        }
        finally { await DisposeHost(app); }
    }

    // ── 8. TopTools data is forwarded from service ─────────────────

    [Fact]
    public async Task Overview_TopToolsData_ForwardedFromService()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var tools = new[]
            {
                new ToolUsageSummaryRow("kql-query", 30, 28, 2, 250.0),
                new ToolUsageSummaryRow("http-check", 15, 15, 0, 120.0),
            };

            svc.Setup(s => s.GetOverviewAsync(null, null, "tenant-d", null, null, (int?)null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DashboardOverviewResponse(
                   new AgentRunsSummaryReport(0, 0, 0, 0, 0, 0, null, null, null, 0.0, null, null),
                   Array.Empty<AgentRunsTrendPoint>(),
                   tools,
                   Array.Empty<RecentRunSummary>()));

            var response = await client.SendAsync(
                Get("/reports/dashboard/overview", tenantId: "tenant-d"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var topTools = doc.RootElement.GetProperty("topTools");

            Assert.Equal(2, topTools.GetArrayLength());
            Assert.Equal("kql-query", topTools[0].GetProperty("toolName").GetString());
        }
        finally { await DisposeHost(app); }
    }

    // ── 9. Invalid fromUtc format → 400 ───────────────────────────

    [Fact]
    public async Task Overview_InvalidFromUtc_Returns400()
    {
        var (app, client, _) = await CreateTestHost();
        try
        {
            var response = await client.SendAsync(
                Get("/reports/dashboard/overview?fromUtc=not-a-date", tenantId: "tenant-a"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally { await DisposeHost(app); }
    }

    // ── 10. fromUtc after toUtc → 400 ─────────────────────────────

    [Fact]
    public async Task Overview_FromAfterTo_Returns400()
    {
        var (app, client, _) = await CreateTestHost();
        try
        {
            var response = await client.SendAsync(
                Get("/reports/dashboard/overview?fromUtc=2025-02-01&toUtc=2025-01-01",
                    tenantId: "tenant-a"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally { await DisposeHost(app); }
    }
}
