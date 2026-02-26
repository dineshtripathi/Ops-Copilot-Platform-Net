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
/// HTTP-level tests for Slice 23: Reporting MVP — read-only safe-actions reports.
/// </summary>
public class ReportingEndpointTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ── Test host factory ───────────────────────────────────────

    private static async Task<(WebApplication App, HttpClient Client, Mock<IReportingQueryService> Svc)>
        CreateTestHost()
    {
        var svc = new Mock<IReportingQueryService>(MockBehavior.Strict);

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton(svc.Object);

        var app = builder.Build();
        app.MapReportingEndpoints();
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

    private static SafeActionsSummaryReport EmptySummary(
        DateTime? from = null, DateTime? to = null)
        => new(0, 0, 0, 0, 0, 0, 0, from, to);

    // ── 1. Summary — OK with defaults ───────────────────────────

    [Fact]
    public async Task Summary_ReturnsOk_WithDefaults()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            svc.Setup(s => s.GetSummaryAsync(null, null, null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new SafeActionsSummaryReport(5, 1, 1, 1, 0, 1, 1, null, null));

            var response = await client.SendAsync(
                Get("/reports/safe-actions/summary"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SafeActionsSummaryReport>(body, JsonOpts);
            Assert.NotNull(result);
            Assert.Equal(5, result.TotalActions);
        }
        finally { await DisposeHost(app); }
    }

    // ── 2. Summary — OK with date range ─────────────────────────

    [Fact]
    public async Task Summary_ReturnsOk_WithDateRange()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            svc.Setup(s => s.GetSummaryAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(EmptySummary());

            var response = await client.SendAsync(
                Get("/reports/safe-actions/summary?fromUtc=2025-01-01&toUtc=2025-06-01"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally { await DisposeHost(app); }
    }

    // ── 3. Summary — OK with tenant filter ──────────────────────

    [Fact]
    public async Task Summary_ReturnsOk_WithTenantId()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            svc.Setup(s => s.GetSummaryAsync(null, null, "t-23", It.IsAny<CancellationToken>()))
               .ReturnsAsync(EmptySummary());

            var response = await client.SendAsync(
                Get("/reports/safe-actions/summary", "t-23"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally { await DisposeHost(app); }
    }

    // ── 4. Summary — 400 when fromUtc invalid ───────────────────

    [Fact]
    public async Task Summary_Returns400_WhenFromUtcInvalid()
    {
        var (app, client, _) = await CreateTestHost();
        try
        {
            var response = await client.SendAsync(
                Get("/reports/safe-actions/summary?fromUtc=not-a-date"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Invalid fromUtc", body);
        }
        finally { await DisposeHost(app); }
    }

    // ── 5. Summary — 400 when toUtc invalid ─────────────────────

    [Fact]
    public async Task Summary_Returns400_WhenToUtcInvalid()
    {
        var (app, client, _) = await CreateTestHost();
        try
        {
            var response = await client.SendAsync(
                Get("/reports/safe-actions/summary?toUtc=bad"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Invalid toUtc", body);
        }
        finally { await DisposeHost(app); }
    }

    // ── 6. Summary — 400 when fromUtc > toUtc ──────────────────

    [Fact]
    public async Task Summary_Returns400_WhenFromAfterTo()
    {
        var (app, client, _) = await CreateTestHost();
        try
        {
            var response = await client.SendAsync(
                Get("/reports/safe-actions/summary?fromUtc=2025-12-01&toUtc=2025-01-01"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("fromUtc must not be after toUtc", body);
        }
        finally { await DisposeHost(app); }
    }

    // ── 7. ByActionType — OK ────────────────────────────────────

    [Fact]
    public async Task ByActionType_ReturnsOk()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var rows = new List<ActionTypeBreakdownRow>
            {
                new("restart_pod", 3, 2, 1),
                new("scale_up",    1, 1, 0)
            };
            svc.Setup(s => s.GetByActionTypeAsync(null, null, null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(rows);

            var response = await client.SendAsync(
                Get("/reports/safe-actions/by-action-type"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<List<ActionTypeBreakdownRow>>(body, JsonOpts);
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
        }
        finally { await DisposeHost(app); }
    }

    // ── 8. ByActionType — OK with tenant filter ─────────────────

    [Fact]
    public async Task ByActionType_ReturnsOk_WithTenantFilter()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            svc.Setup(s => s.GetByActionTypeAsync(null, null, "t-23", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new List<ActionTypeBreakdownRow>());

            var response = await client.SendAsync(
                Get("/reports/safe-actions/by-action-type", "t-23"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally { await DisposeHost(app); }
    }

    // ── 9. ByTenant — OK ────────────────────────────────────────

    [Fact]
    public async Task ByTenant_ReturnsOk()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            var rows = new List<TenantBreakdownRow>
            {
                new("tenant-a", 10, 8, 2),
                new("tenant-b",  5, 5, 0)
            };
            svc.Setup(s => s.GetByTenantAsync(null, null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(rows);

            var response = await client.SendAsync(
                Get("/reports/safe-actions/by-tenant"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<List<TenantBreakdownRow>>(body, JsonOpts);
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
        }
        finally { await DisposeHost(app); }
    }

    // ── 10. Recent — OK default limit ───────────────────────────

    [Fact]
    public async Task Recent_ReturnsOk_DefaultLimit()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            svc.Setup(s => s.GetRecentAsync(20, null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new List<RecentActionRow>());

            var response = await client.SendAsync(
                Get("/reports/safe-actions/recent"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally { await DisposeHost(app); }
    }

    // ── 11. Recent — OK custom limit ────────────────────────────

    [Fact]
    public async Task Recent_ReturnsOk_CustomLimit()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            svc.Setup(s => s.GetRecentAsync(5, null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new List<RecentActionRow>());

            var response = await client.SendAsync(
                Get("/reports/safe-actions/recent?limit=5"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally { await DisposeHost(app); }
    }

    // ── 12. Recent — clamps limit to max 100 ────────────────────

    [Fact]
    public async Task Recent_ClampsLimitToMax100()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            svc.Setup(s => s.GetRecentAsync(100, null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new List<RecentActionRow>());

            var response = await client.SendAsync(
                Get("/reports/safe-actions/recent?limit=999"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            // The mock expects exactly limit=100, so this proves clamping worked
        }
        finally { await DisposeHost(app); }
    }

    // ── 13. Recent — OK with tenant filter ──────────────────────

    [Fact]
    public async Task Recent_ReturnsOk_WithTenantFilter()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            svc.Setup(s => s.GetRecentAsync(20, "t-23", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new List<RecentActionRow>());

            var response = await client.SendAsync(
                Get("/reports/safe-actions/recent", "t-23"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally { await DisposeHost(app); }
    }

    // ── 14. Recent — returns empty array when no data ───────────

    [Fact]
    public async Task Recent_ReturnsEmptyArray_WhenNoData()
    {
        var (app, client, svc) = await CreateTestHost();
        try
        {
            svc.Setup(s => s.GetRecentAsync(20, null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new List<RecentActionRow>());

            var response = await client.SendAsync(
                Get("/reports/safe-actions/recent"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<List<RecentActionRow>>(body, JsonOpts);
            Assert.NotNull(result);
            Assert.Empty(result);
        }
        finally { await DisposeHost(app); }
    }
}
