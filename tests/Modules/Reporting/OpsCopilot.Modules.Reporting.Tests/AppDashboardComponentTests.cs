using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;
using OpsCopilot.Reporting.Presentation.Blazor.Components;

namespace OpsCopilot.Modules.Reporting.Tests;

/// <summary>
/// AC-73–82: HTTP-level integration tests for the Blazor SSR operator dashboard.
/// Each test spins up a lightweight TestServer with the Blazor pipeline and a
/// strict mock of IDashboardQueryService, then asserts on the rendered HTML.
/// </summary>
public sealed class AppDashboardComponentTests
{
    // ───── shared helpers ────────────────────────────────────────────────

    private static async Task<(WebApplication App, HttpClient Client, Mock<IDashboardQueryService> Svc)>
        CreateBlazorTestHost(Action<Mock<IDashboardQueryService>> configure)
    {
        var svc = new Mock<IDashboardQueryService>(MockBehavior.Strict);
        configure(svc);

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddRazorComponents();
        builder.Services.AddSingleton(svc.Object);

        var app = builder.Build();
        app.UseAntiforgery();
        app.MapRazorComponents<App>();

        await app.StartAsync();
        return (app, app.GetTestClient(), svc);
    }

    private static DashboardOverviewResponse EmptyOverview() =>
        new(
            Summary:    new AgentRunsSummaryReport(0, 0, 0, 0, 0, 0, null, null, null, 0.0, null, null),
            Trend:      Array.Empty<AgentRunsTrendPoint>(),
            TopTools:   Array.Empty<ToolUsageSummaryRow>(),
            RecentRuns: Array.Empty<RecentRunSummary>());

    private static DashboardOverviewResponse OverviewWithObservability(Guid runId) =>
        new(
            Summary:    new AgentRunsSummaryReport(3, 2, 1, 0, 0, 0, null, null, null, 0.5, null, null),
            Trend:      Array.Empty<AgentRunsTrendPoint>(),
            TopTools:   Array.Empty<ToolUsageSummaryRow>(),
            RecentRuns: new[] { new RecentRunSummary(runId, null, "Failed", "fp-obs", new DateTimeOffset(2025, 4, 2, 10, 0, 0, TimeSpan.Zero), null) },
            ObservabilitySpotlight: new ObservabilityEvidenceSpotlight(
                RunId: runId,
                Status: "Failed",
                CreatedAtUtc: new DateTimeOffset(2025, 4, 2, 10, 0, 0, TimeSpan.Zero),
                Evidence: new ObservabilityEvidenceSummary(
                    Source: "app-insights",
                    CollectorCount: 2,
                    SuccessfulCollectors: 1,
                    FailedCollectors: 1,
                    CollectorSummaries:
                    [
                        new ObservabilityEvidenceCollectorSummary(
                            CollectorId: "top-exceptions",
                            Title: "Top Exceptions",
                            RowCount: 1,
                            Status: "Ready",
                            Highlights: ["NullReferenceException (42) - Object reference not set"]),
                        new ObservabilityEvidenceCollectorSummary(
                            CollectorId: "failed-requests",
                            Title: "Failed Requests",
                            RowCount: 0,
                            Status: "Unavailable",
                            Highlights: [],
                            ErrorMessage: "workspace query timeout")
                    ])));

    private static DashboardOverviewResponse OverviewWithLiveObservability() =>
        new(
            Summary:    new AgentRunsSummaryReport(3, 2, 1, 0, 0, 0, null, null, null, 0.5, null, null),
            Trend:      Array.Empty<AgentRunsTrendPoint>(),
            TopTools:   Array.Empty<ToolUsageSummaryRow>(),
            RecentRuns: Array.Empty<RecentRunSummary>(),
            LiveObservabilityEvidence: new ObservabilityEvidenceSummary(
                Source: "app-insights",
                CollectorCount: 1,
                SuccessfulCollectors: 1,
                FailedCollectors: 0,
                CollectorSummaries:
                [
                    new ObservabilityEvidenceCollectorSummary(
                        CollectorId: "top-exceptions",
                        Title: "Top Exceptions",
                        RowCount: 1,
                        Status: "Ready",
                        Highlights: ["Flurl.Http.FlurlHttpException (303) - api.beaconcrm.org"])
                ],
                CoverageStatus: "live-signal-detected",
                IsActionable: true,
                Recommendations:
                [
                    "Prioritize collector rows with highest failure/exception counts."
                ]),
            LiveImpactEvidence: new LiveImpactEvidenceSummary(
                Source: "app-insights",
                BlastRadius: new BlastRadiusSummary(1, 2, 4, 1),
                ActivitySignals: new ActivitySignalSummary(9, 3, 1, 0, 2),
                CoverageStatus: "live-impact-detected",
                IsActionable: true,
                SuccessfulCollectors: 2,
                FailedCollectors: 0,
                Recommendations:
                [
                    "Prioritize resources in the live impact section for immediate triage."
                ]),
            TenantEstate: new TenantEstateSummary(
                TenantId: "4a72b866-99a4-4388-b881-cef9c8480b1c",
                AccessibleSubscriptionCount: 4,
                ActiveSubscriptionCount: 4,
                Subscriptions:
                [
                    new AzureSubscriptionSummary("5734706f-a5ee-405c-998e-b6dc2bfade69", "Azure AI", "Enabled"),
                    new AzureSubscriptionSummary("a143fdc9-ae6c-4123-abfb-56f36bb9f53d", "Dinesh Azure AI Subscriptions", "Enabled"),
                    new AzureSubscriptionSummary("bd27a79c-de25-4097-a874-3bb35f2b926a", "Visual Studio Enterprise", "Enabled"),
                    new AzureSubscriptionSummary("b20a7294-6951-4107-88df-d7d320218670", "Visual Studio Enterprise with MSDN", "Enabled")
                ]),
            DataFreshness: new DashboardDataFreshness(
                LiveEvaluatedAtUtc: new DateTimeOffset(2026, 3, 21, 18, 0, 0, TimeSpan.Zero),
                LatestHistoricalRunAtUtc: new DateTimeOffset(2026, 2, 21, 17, 36, 59, TimeSpan.Zero),
                HistoricalDataIsStale: true));

    private static DashboardOverviewResponse OverviewWithLiveImpactDiagnostic() =>
        new(
            Summary:    new AgentRunsSummaryReport(3, 2, 1, 0, 0, 0, null, null, null, 0.5, null, null),
            Trend:      Array.Empty<AgentRunsTrendPoint>(),
            TopTools:   Array.Empty<ToolUsageSummaryRow>(),
            RecentRuns: Array.Empty<RecentRunSummary>(),
            LiveObservabilityEvidence: new ObservabilityEvidenceSummary(
                Source: "app-insights",
                CollectorCount: 1,
                SuccessfulCollectors: 1,
                FailedCollectors: 0,
                CollectorSummaries:
                [
                    new ObservabilityEvidenceCollectorSummary(
                        CollectorId: "top-exceptions",
                        Title: "Top Exceptions",
                        RowCount: 0,
                        Status: "Ready",
                        Highlights: [])
                ]),
            LiveImpactEvidence: new LiveImpactEvidenceSummary(
                Source: "app-insights",
                BlastRadius: null,
                ActivitySignals: null,
                Diagnostic: "Live impact collectors succeeded but returned zero rows for the current workspace/time window.",
                CoverageStatus: "live-data-no-impact",
                IsActionable: false,
                SuccessfulCollectors: 2,
                FailedCollectors: 0,
                Recommendations:
                [
                    "Widen lookback window or confirm the active incident timestamp/window."
                ]),
            DataFreshness: new DashboardDataFreshness(
                LiveEvaluatedAtUtc: new DateTimeOffset(2026, 3, 21, 18, 0, 0, TimeSpan.Zero),
                LatestHistoricalRunAtUtc: new DateTimeOffset(2026, 2, 21, 17, 36, 59, TimeSpan.Zero),
                HistoricalDataIsStale: true));

    // ───── AC-73: missing tenantId → 200 + required-panel ───────────────

    [Fact]
    public async Task BlazorDashboard_MissingTenantId_Returns200WithRequiredMessage()
    {
        // strict mock with no setup — service must not be called
        var (app, client, _) = await CreateBlazorTestHost(_ => { });
        await using (app)
        {
            var response = await client.GetAsync("/app/dashboard");
            var body     = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Tenant ID Required", body, StringComparison.Ordinal);
            Assert.Contains("oc-error-panel",     body, StringComparison.Ordinal);
        }
    }

    // ───── AC-74: valid tenantId → service called with correct id ────────

    [Fact]
    public async Task BlazorDashboard_ValidTenantId_ServiceCalledWithCorrectTenantId()
    {
        var (app, client, svc) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "tenant-abc",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(EmptyOverview()));

        await using (app)
        {
            await client.GetAsync("/app/dashboard?tenantId=tenant-abc");

            svc.Verify(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "tenant-abc",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    // ───── AC-75: run count rendered ─────────────────────────────────────

    [Fact]
    public async Task BlazorDashboard_ValidTenantId_RendersRunCount()
    {
        var overview = new DashboardOverviewResponse(
            Summary:    new AgentRunsSummaryReport(42, 30, 5, 7, 0, 0, null, null, null, 0.0, null, null),
            Trend:      Array.Empty<AgentRunsTrendPoint>(),
            TopTools:   Array.Empty<ToolUsageSummaryRow>(),
            RecentRuns: Array.Empty<RecentRunSummary>());

        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "t1",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(overview));

        await using (app)
        {
            var body = await (await client.GetAsync("/app/dashboard?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("42", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-76: recent-run links rendered ──────────────────────────────

    [Fact]
    public async Task BlazorDashboard_ValidTenantId_RendersRecentRunLinks()
    {
        var runId = Guid.NewGuid();
        var overview = new DashboardOverviewResponse(
            Summary:    new AgentRunsSummaryReport(1, 1, 0, 0, 0, 0, null, null, null, 0.0, null, null),
            Trend:      Array.Empty<AgentRunsTrendPoint>(),
            TopTools:   Array.Empty<ToolUsageSummaryRow>(),
            RecentRuns: new[]
            {
                new RecentRunSummary(runId, null, "Completed", null, DateTimeOffset.UtcNow, null)
            });

        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "t1",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(overview));

        await using (app)
        {
            var body = await (await client.GetAsync("/app/dashboard?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains($"/app/runs/{runId}?tenantId=t1", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task BlazorDashboard_WithObservabilitySpotlight_RendersAppInsightsSection()
    {
        var runId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "t1",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(OverviewWithObservability(runId)));

        await using (app)
        {
            var body = await (await client.GetAsync("/app/dashboard?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("App Insights spotlight", body, StringComparison.Ordinal);
            Assert.Contains("Top Exceptions", body, StringComparison.Ordinal);
            Assert.Contains("workspace query timeout", body, StringComparison.Ordinal);
            Assert.Contains($"/app/runs/{runId}?tenantId=t1", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task BlazorDashboard_WithLiveObservability_RendersLiveAppInsightsSection()
    {
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "t1",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(OverviewWithLiveObservability()));

        await using (app)
        {
            var body = await (await client.GetAsync("/app/dashboard?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("App Insights live summary", body, StringComparison.Ordinal);
            Assert.Contains("Flurl.Http.FlurlHttpException", body, StringComparison.Ordinal);
            Assert.DoesNotContain("run-", body, StringComparison.Ordinal);
            Assert.Contains("live-signal-detected", body, StringComparison.Ordinal);
            Assert.Contains("Prioritize collector rows", body, StringComparison.Ordinal);
            Assert.Contains("Live feed first, historical context second", body, StringComparison.Ordinal);
            Assert.Contains("Historical DB projections are stale", body, StringComparison.Ordinal);
            Assert.Contains("Live incident impact feed", body, StringComparison.Ordinal);
            Assert.Contains("Live Impacted Resources", body, StringComparison.Ordinal);
            Assert.Contains("Live Policy Events", body, StringComparison.Ordinal);
            Assert.Contains("live-impact-detected", body, StringComparison.Ordinal);
            Assert.Contains("Actionable", body, StringComparison.Ordinal);
            Assert.Contains("Tenant Subscriptions", body, StringComparison.Ordinal);
            Assert.Contains("Live Impact Scope", body, StringComparison.Ordinal);
            Assert.Contains("1 / 4", body, StringComparison.Ordinal);
            Assert.Contains("Azure AI", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task BlazorDashboard_WithLiveImpactDiagnostic_RendersEmptyReason()
    {
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "t1",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(OverviewWithLiveImpactDiagnostic()));

        await using (app)
        {
            var body = await (await client.GetAsync("/app/dashboard?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("Live incident impact feed", body, StringComparison.Ordinal);
            Assert.Contains("returned zero rows", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("live-data-no-impact", body, StringComparison.Ordinal);
            Assert.Contains("Widen lookback window", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-77: service throws → error panel ───────────────────────────

    [Fact]
    public async Task BlazorDashboard_ServiceThrows_RendersErrorPanel()
    {
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "t1",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom")));

        await using (app)
        {
            var response = await client.GetAsync("/app/dashboard?tenantId=t1");
            var body     = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Dashboard Unavailable", body, StringComparison.Ordinal);
            Assert.Contains("oc-error-panel",        body, StringComparison.Ordinal);
        }
    }

    // ───── AC-78: exception details not leaked ───────────────────────────

    [Fact]
    public async Task BlazorDashboard_ServiceThrows_NoExceptionDetailsInHtml()
    {
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "t1",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom")));

        await using (app)
        {
            var body = await (await client.GetAsync("/app/dashboard?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
            Assert.DoesNotContain("boom",                      body, StringComparison.Ordinal);
            Assert.DoesNotContain("StackTrace",                body, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ───── AC-79: no POST form in rendered output ─────────────────────────

    [Fact]
    public async Task BlazorDashboard_NoPostFormInOutput()
    {
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "t1",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(EmptyOverview()));

        await using (app)
        {
            var body = await (await client.GetAsync("/app/dashboard?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("method=\"post\"", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ───── AC-80: date filters forwarded to service ───────────────────────

    [Fact]
    public async Task BlazorDashboard_DateFilters_PassedToService()
    {
        var (app, client, svc) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetOverviewAsync(
                    It.Is<DateTime?>(d => d.HasValue && d.Value == new DateTime(2025, 1, 1)),
                    It.Is<DateTime?>(d => d.HasValue && d.Value == new DateTime(2025, 1, 31)),
                    "t1",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(EmptyOverview()));

        await using (app)
        {
            await client.GetAsync("/app/dashboard?tenantId=t1&fromUtc=2025-01-01&toUtc=2025-01-31");

            svc.Verify(s => s.GetOverviewAsync(
                    It.Is<DateTime?>(d => d.HasValue && d.Value == new DateTime(2025, 1, 1)),
                    It.Is<DateTime?>(d => d.HasValue && d.Value == new DateTime(2025, 1, 31)),
                    "t1",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    // ───── AC-81: empty runs list → no-results message ───────────────────

    [Fact]
    public async Task BlazorDashboard_EmptyRunsList_RendersNoneFoundMessage()
    {
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "t1",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(EmptyOverview()));

        await using (app)
        {
            var body = await (await client.GetAsync("/app/dashboard?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("No runs found for the selected filters.", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-82: cross-tenant isolation ─────────────────────────────────

    [Fact]
    public async Task BlazorDashboard_CrossTenant_ServiceCalledWithDistinctTenantIds()
    {
        var (app, client, svc) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(EmptyOverview()));

        await using (app)
        {
            await client.GetAsync("/app/dashboard?tenantId=tenant-a");
            await client.GetAsync("/app/dashboard?tenantId=tenant-b");

            svc.Verify(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "tenant-a",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            svc.Verify(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "tenant-b",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    // ───── AC-102: run links carry active filters ─────────────────────────

    [Fact]
    public async Task BlazorDashboard_RunLinks_CarryActiveFilters()
    {
        var runId = Guid.NewGuid();
        var overview = new DashboardOverviewResponse(
            Summary:    new AgentRunsSummaryReport(1, 1, 0, 0, 0, 0, null, null, null, 0.0, null, null),
            Trend:      Array.Empty<AgentRunsTrendPoint>(),
            TopTools:   Array.Empty<ToolUsageSummaryRow>(),
            RecentRuns: new[] { new RecentRunSummary(runId, null, "Completed", null, DateTimeOffset.UtcNow, null) });

        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "t1",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(overview));

        await using (app)
        {
            var body = await (await client.GetAsync(
                    "/app/dashboard?tenantId=t1&fromUtc=2024-01-01&toUtc=2024-01-31"))
                .Content.ReadAsStringAsync();

            Assert.Contains(
                $"/app/runs/{runId}?tenantId=t1&amp;fromUtc=2024-01-01&amp;toUtc=2024-01-31",
                body, StringComparison.Ordinal);
        }
    }

    // ───── AC-103: run links omit filters when absent ─────────────────────

    [Fact]
    public async Task BlazorDashboard_RunLinks_OmitFiltersWhenAbsent()
    {
        var runId = Guid.NewGuid();
        var overview = new DashboardOverviewResponse(
            Summary:    new AgentRunsSummaryReport(1, 1, 0, 0, 0, 0, null, null, null, 0.0, null, null),
            Trend:      Array.Empty<AgentRunsTrendPoint>(),
            TopTools:   Array.Empty<ToolUsageSummaryRow>(),
            RecentRuns: new[] { new RecentRunSummary(runId, null, "Completed", null, DateTimeOffset.UtcNow, null) });

        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "t1",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(overview));

        await using (app)
        {
            var body = await (await client.GetAsync("/app/dashboard?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains($"/app/runs/{runId}?tenantId=t1", body, StringComparison.Ordinal);
            Assert.DoesNotContain(
                $"/app/runs/{runId}?tenantId=t1&amp;fromUtc=",
                body, StringComparison.Ordinal);
        }
    }

    // ───── AC-104: filter values are XSS-safe in run links ───────────────

    [Fact]
    public async Task BlazorDashboard_FilterValues_XssSafeInRunLinks()
    {
        var runId = Guid.NewGuid();
        var overview = new DashboardOverviewResponse(
            Summary:    new AgentRunsSummaryReport(1, 1, 0, 0, 0, 0, null, null, null, 0.0, null, null),
            Trend:      Array.Empty<AgentRunsTrendPoint>(),
            TopTools:   Array.Empty<ToolUsageSummaryRow>(),
            RecentRuns: new[] { new RecentRunSummary(runId, null, "Completed", null, DateTimeOffset.UtcNow, null) });

        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "t1",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(overview));

        await using (app)
        {
            var body = await (await client.GetAsync(
                    "/app/dashboard?tenantId=t1&fromUtc=%3Cscript%3Ealert(1)%3C%2Fscript%3E"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("<script>alert(1)</script>", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ───── AC-111: run links carry status/sort/limit ────────────────────

    [Fact]
    public async Task BlazorDashboard_RunLinks_CarryStatusSortLimit()
    {
        var runId = Guid.NewGuid();
        var overview = new DashboardOverviewResponse(
            Summary:    new AgentRunsSummaryReport(1, 1, 0, 0, 0, 0, null, null, null, 0.0, null, null),
            Trend:      Array.Empty<AgentRunsTrendPoint>(),
            TopTools:   Array.Empty<ToolUsageSummaryRow>(),
            RecentRuns: new[] { new RecentRunSummary(runId, null, "Completed", null, DateTimeOffset.UtcNow, null) });

        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "t1",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(overview));

        await using (app)
        {
            var body = await (await client.GetAsync(
                    "/app/dashboard?tenantId=t1&status=Completed&sort=newest&limit=50"))
                .Content.ReadAsStringAsync();

            Assert.Contains(
                $"/app/runs/{runId}?tenantId=t1&amp;status=Completed&amp;sort=newest&amp;limit=50",
                body, StringComparison.Ordinal);
        }
    }

    // ───── AC-112: service receives parsed status/sort/limit ────────────

    [Fact]
    public async Task BlazorDashboard_ServiceReceivesStatusSortLimit()
    {
        var runId = Guid.NewGuid();
        var overview = new DashboardOverviewResponse(
            Summary:    new AgentRunsSummaryReport(1, 1, 0, 0, 0, 0, null, null, null, 0.0, null, null),
            Trend:      Array.Empty<AgentRunsTrendPoint>(),
            TopTools:   Array.Empty<ToolUsageSummaryRow>(),
            RecentRuns: new[] { new RecentRunSummary(runId, null, "Completed", null, DateTimeOffset.UtcNow, null) });

        Mock<IDashboardQueryService>? capturedMock = null;
        var (app, client, _) = await CreateBlazorTestHost(mock =>
        {
            capturedMock = mock;
            mock.Setup(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "t1",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(overview);
        });

        await using (app)
        {
            await client.GetAsync("/app/dashboard?tenantId=t1&status=Completed&sort=newest&limit=50");

            capturedMock!.Verify(s => s.GetOverviewAsync(
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                "t1", "Completed", "newest", 50,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    // ───── AC-84a: invalid status → service NOT called, invalid-input panel ─

    [Fact]
    public async Task BlazorDashboard_InvalidStatus_ServiceNotCalled_RendersInvalidInputPanel()
    {
        // strict mock with no setup — service must not be called
        var (app, client, _) = await CreateBlazorTestHost(_ => { });
        await using (app)
        {
            var response = await client.GetAsync("/app/dashboard?tenantId=t1&status=InvalidValue");
            var body     = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("oc-error-panel",       body, StringComparison.Ordinal);
            Assert.Contains("Invalid Filter Value", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-84b: invalid sort → service NOT called, invalid-input panel ──

    [Fact]
    public async Task BlazorDashboard_InvalidSort_ServiceNotCalled_RendersInvalidInputPanel()
    {
        // strict mock with no setup — service must not be called
        var (app, client, _) = await CreateBlazorTestHost(_ => { });
        await using (app)
        {
            var response = await client.GetAsync("/app/dashboard?tenantId=t1&sort=InvalidSort");
            var body     = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("oc-error-panel",       body, StringComparison.Ordinal);
            Assert.Contains("Invalid Filter Value", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-84c: valid status forwarded to service ─────────────────────

    [Fact]
    public async Task BlazorDashboard_ValidStatus_ServiceCalledWithStatus()
    {
        Mock<IDashboardQueryService>? capturedMock = null;
        var (app, client, _) = await CreateBlazorTestHost(mock =>
        {
            capturedMock = mock;
            mock.Setup(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "t1",
                    "Failed",
                    It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(EmptyOverview());
        });

        await using (app)
        {
            await client.GetAsync("/app/dashboard?tenantId=t1&status=Failed");

            capturedMock!.Verify(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "t1", "Failed",
                    It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    // ───── AC-84d: valid sort forwarded to service ────────────────────────

    [Fact]
    public async Task BlazorDashboard_ValidSort_ServiceCalledWithSort()
    {
        Mock<IDashboardQueryService>? capturedMock = null;
        var (app, client, _) = await CreateBlazorTestHost(mock =>
        {
            capturedMock = mock;
            mock.Setup(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "t1",
                    It.IsAny<string?>(),
                    "oldest",
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(EmptyOverview());
        });

        await using (app)
        {
            await client.GetAsync("/app/dashboard?tenantId=t1&sort=oldest");

            capturedMock!.Verify(s => s.GetOverviewAsync(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    "t1",
                    It.IsAny<string?>(), "oldest", It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    // ───── AC-84e: wrong-case status rejected (Ordinal comparison) ───────

    [Fact]
    public async Task BlazorDashboard_WrongCaseStatus_RendersInvalidInputPanel()
    {
        // "completed" != "Completed" under StringComparer.Ordinal
        // strict mock with no setup — service must not be called
        var (app, client, _) = await CreateBlazorTestHost(_ => { });
        await using (app)
        {
            var response = await client.GetAsync("/app/dashboard?tenantId=t1&status=completed");
            var body     = await response.Content.ReadAsStringAsync();

            Assert.Contains("oc-error-panel",       body, StringComparison.Ordinal);
            Assert.Contains("Invalid Filter Value", body, StringComparison.Ordinal);
        }
    }
}
