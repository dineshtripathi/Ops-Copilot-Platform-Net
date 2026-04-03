using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;
using OpsCopilot.Reporting.Presentation.Endpoints;
using Xunit;

namespace OpsCopilot.Integration.Tests;

/// <summary>
/// E2E tests for the Reporting dashboard endpoint:
/// GET /reports/dashboard/overview with in-memory TestServer.
/// </summary>
public sealed class DashboardReportingE2ETests
{
    private static readonly DashboardOverviewResponse CannedOverview = new(
        Summary: new AgentRunsSummaryReport(
            TotalRuns: 10, Completed: 8, Failed: 1, Degraded: 1,
            Pending: 0, Running: 0, AvgDurationMs: 150.0, AvgTotalTokens: 500.0,
            TotalEstimatedCost: 0.5m, CitationCoverageRate: 0.95,
            FromUtc: DateTime.UtcNow.AddDays(-7), ToUtc: DateTime.UtcNow),
        Trend: Array.Empty<AgentRunsTrendPoint>(),
        TopTools: Array.Empty<ToolUsageSummaryRow>(),
        RecentRuns: Array.Empty<RecentRunSummary>());

    private static async Task<(WebApplication App, HttpClient Client)> CreateTestHost()
    {
        var dashboardService = new Mock<IDashboardQueryService>();
        dashboardService
            .Setup(s => s.GetOverviewAsync(
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CannedOverview);

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddAuthentication(E2ETestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, E2ETestAuthHandler>(
                E2ETestAuthHandler.SchemeName, null);
        builder.Services.AddAuthorization();

        builder.Services.AddSingleton(dashboardService.Object);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapDashboardEndpoints();
        await app.StartAsync();

        return (app, app.GetTestClient());
    }

    [Fact]
    public async Task Get_overview_with_tenant_header_returns_200()
    {
        var (app, client) = await CreateTestHost();
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/reports/dashboard/overview");
            request.Headers.Add("x-tenant-id", "tenant-e2e-003");

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<DashboardOverviewResponse>();
            Assert.NotNull(body);
            Assert.NotNull(body!.Summary);
            Assert.Equal(10, body.Summary.TotalRuns);
            Assert.Equal(8, body.Summary.Completed);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Get_overview_without_tenant_header_returns_400()
    {
        var (app, client) = await CreateTestHost();
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/reports/dashboard/overview");
            // Deliberately omit x-tenant-id header

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
