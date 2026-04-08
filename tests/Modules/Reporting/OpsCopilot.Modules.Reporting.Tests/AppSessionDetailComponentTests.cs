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
/// AC-93–101: HTTP-level integration tests for the Blazor SSR session-detail page.
/// Each test spins up a lightweight TestServer with the Blazor pipeline and a
/// strict mock of IAgentRunsReportingQueryService, then asserts on the rendered HTML.
/// </summary>
public sealed class AppSessionDetailComponentTests
{
    // ───── shared helpers ────────────────────────────────────────────────

    private static async Task<(WebApplication App, HttpClient Client, Mock<IAgentRunsReportingQueryService> Svc)>
        CreateBlazorTestHost(Action<Mock<IAgentRunsReportingQueryService>> configure)
    {
        var svc = new Mock<IAgentRunsReportingQueryService>(MockBehavior.Strict);
        configure(svc);

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddRazorComponents();
        builder.Services.AddSingleton(svc.Object);

        // AppSessionDetail.razor injects IAgentRunsReportingQueryService, but the
        // Blazor router also resolves IDashboardQueryService for AppDashboard.razor.
        // Register a permissive stub so the DI container resolves it without errors.
        var dashStub = new Mock<IDashboardQueryService>(MockBehavior.Loose);
        builder.Services.AddSingleton(dashStub.Object);

        // Slice 202: Routes.razor now uses AuthorizeRouteView which requires auth services.
        // FakeAuthStateProvider always returns authenticated so tests render pages normally.
        builder.Services.AddAuthentication("Test")
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
                       TestAuthHandler>("Test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider,
            FakeAuthStateProvider>();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddHttpContextAccessor();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAntiforgery();
        app.MapRazorComponents<App>();

        await app.StartAsync();
        return (app, app.GetTestClient(), svc);
    }


    private static readonly Guid Run1Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Run2Id = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private static SessionDetailResponse SampleDetail(Guid sessionId) =>
        new(
            SessionId: sessionId,
            Runs: new[]
            {
                new RecentRunSummary(
                    RunId:            Run1Id,
                    SessionId:        sessionId,
                    Status:           "Completed",
                    AlertFingerprint: "fp-run1",
                    CreatedAtUtc:     new DateTimeOffset(2025, 4, 1, 9, 0, 0, TimeSpan.Zero),
                    CompletedAtUtc:   new DateTimeOffset(2025, 4, 1, 9, 0, 45, TimeSpan.Zero)),
                new RecentRunSummary(
                    RunId:            Run2Id,
                    SessionId:        sessionId,
                    Status:           "Failed",
                    AlertFingerprint: null,
                    CreatedAtUtc:     new DateTimeOffset(2025, 4, 1, 9, 1, 0, TimeSpan.Zero),
                    CompletedAtUtc:   null)
            },
            Briefing: new SessionBriefing(
                RunCount:            2,
                IsIsolated:          false,
                StatusPattern:       "degrading",
                FingerprintPattern:  "mixed",
                DominantFingerprint: "fp-run1",
                SequenceConclusion:  "Worsening"));

    private static SessionDetailResponse SampleDetailWithObservability(Guid sessionId) =>
        SampleDetail(sessionId) with
        {
            ObservabilitySpotlight = new ObservabilityEvidenceSpotlight(
                RunId: Run2Id,
                Status: "Failed",
                CreatedAtUtc: new DateTimeOffset(2025, 4, 1, 9, 1, 0, TimeSpan.Zero),
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
                    ]))
        };

    // ───── AC-93: missing tenantId → 200 + required-panel ───────────────

    [Fact]
    public async Task BlazorSessionDetail_MissingTenantId_Returns200WithRequiredMessage()
    {
        var sessionId = Guid.NewGuid();
        // strict mock with no setup — service must not be called
        var (app, client, _) = await CreateBlazorTestHost(_ => { });
        await using (app)
        {
            var response = await client.GetAsync($"/app/sessions/{sessionId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("oc-error-panel", html);
            Assert.Contains("Tenant ID Required", html);
        }
    }

    // ───── AC-94: service called with correct session ID and tenant ID ───

    [Fact]
    public async Task BlazorSessionDetail_ValidArgs_ServiceCalledWithCorrectIds()
    {
        var sessionId = Guid.NewGuid();
        var (app, client, svc) = await CreateBlazorTestHost(m =>
            m.Setup(x => x.GetSessionDetailAsync(sessionId, "tenant-abc", It.IsAny<CancellationToken>()))
             .ReturnsAsync(SampleDetail(sessionId)));
        await using (app)
        {
            await client.GetAsync($"/app/sessions/{sessionId}?tenantId=tenant-abc");

            svc.Verify(x => x.GetSessionDetailAsync(sessionId, "tenant-abc", It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    // ───── AC-95: valid session → renders metadata section ───────────────

    [Fact]
    public async Task BlazorSessionDetail_ValidSession_RendersMetadataSection()
    {
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(m =>
            m.Setup(x => x.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(SampleDetail(sessionId)));
        await using (app)
        {
            var html = await (await client.GetAsync($"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-session-metadata", html);
            Assert.Contains("Session Detail", html);
            Assert.Contains(sessionId.ToString(), html);
            Assert.Contains("Run Count", html);
            Assert.Contains("2", html);
        }
    }

    // ───── AC-96: valid session → renders run-progression table ──────────

    [Fact]
    public async Task BlazorSessionDetail_ValidSession_RendersRunProgressionTable()
    {
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(m =>
            m.Setup(x => x.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(SampleDetail(sessionId)));
        await using (app)
        {
            var html = await (await client.GetAsync($"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-run-progression", html);
            Assert.Contains("Run Progression Timeline", html);
            Assert.Contains("Completed", html);
            Assert.Contains("Failed", html);
            Assert.Contains("fp-run1", html);
            Assert.Contains("2025-04-01", html);
            Assert.Contains("45.0s", html);
        }
    }

    // ───── AC-97: run links point to Blazor app run page ─────────────────

    [Fact]
    public async Task BlazorSessionDetail_RenderRunLinks_PointToBlazorRunPage()
    {
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(m =>
            m.Setup(x => x.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(SampleDetail(sessionId)));
        await using (app)
        {
            var html = await (await client.GetAsync($"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains($"/app/runs/{Run1Id}?tenantId=t1", html);
            Assert.Contains($"/app/runs/{Run2Id}?tenantId=t1", html);
        }
    }

    [Fact]
    public async Task BlazorSessionDetail_WithObservabilitySpotlight_RendersAppInsightsSection()
    {
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(m =>
            m.Setup(x => x.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(SampleDetailWithObservability(sessionId)));
        await using (app)
        {
            var html = await (await client.GetAsync($"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("Session App Insights evidence", html, StringComparison.Ordinal);
            Assert.Contains("Top Exceptions", html, StringComparison.Ordinal);
            Assert.Contains("workspace query timeout", html, StringComparison.Ordinal);
            Assert.Contains($"/app/runs/{Run2Id}?tenantId=t1", html, StringComparison.Ordinal);
        }
    }

    // ───── AC-98: service returns null → not-found panel ─────────────────

    [Fact]
    public async Task BlazorSessionDetail_NotFound_RendersNotFoundPanel()
    {
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(m =>
            m.Setup(x => x.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
             .ReturnsAsync((SessionDetailResponse?)null));
        await using (app)
        {
            var response = await client.GetAsync($"/app/sessions/{sessionId}?tenantId=t1");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("oc-not-found-panel", html);
            Assert.Contains("Session Not Found", html);
        }
    }

    // ───── AC-99: service throws → error panel, no leaked details ────────

    [Fact]
    public async Task BlazorSessionDetail_ServiceThrows_RendersErrorPanelWithNoLeakedDetails()
    {
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(m =>
            m.Setup(x => x.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("internal-secret: conn=Server=prod;pwd=xyz")));
        await using (app)
        {
            var response = await client.GetAsync($"/app/sessions/{sessionId}?tenantId=t1");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("oc-error-panel", html);
            Assert.Contains("Session Detail Unavailable", html);
            Assert.DoesNotContain("internal-secret", html);
            Assert.DoesNotContain("InvalidOperationException", html);
        }
    }

    // ───── AC-100: no POST form in rendered output ────────────────────────

    [Fact]
    public async Task BlazorSessionDetail_NoPostFormInOutput()
    {
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(m =>
            m.Setup(x => x.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(SampleDetail(sessionId)));
        await using (app)
        {
            var html = await (await client.GetAsync($"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("method=\"post\"", html);
        }
    }

    // ───── AC-101: cross-tenant isolation — each tenant gets its own call ─

    [Fact]
    public async Task BlazorSessionDetail_CrossTenant_ServiceCalledWithDistinctTenantIds()
    {
        var sessionId = Guid.NewGuid();
        var (app, client, svc) = await CreateBlazorTestHost(m =>
        {
            m.Setup(x => x.GetSessionDetailAsync(sessionId, "tenant-a", It.IsAny<CancellationToken>()))
             .ReturnsAsync(SampleDetail(sessionId));
            m.Setup(x => x.GetSessionDetailAsync(sessionId, "tenant-b", It.IsAny<CancellationToken>()))
             .ReturnsAsync(SampleDetail(sessionId));
        });
        await using (app)
        {
            await client.GetAsync($"/app/sessions/{sessionId}?tenantId=tenant-a");
            await client.GetAsync($"/app/sessions/{sessionId}?tenantId=tenant-b");

            svc.Verify(x => x.GetSessionDetailAsync(sessionId, "tenant-a", It.IsAny<CancellationToken>()), Times.Once);
            svc.Verify(x => x.GetSessionDetailAsync(sessionId, "tenant-b", It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    // ───── AC-108: back link carries active filters ─────────────────────

    [Fact]
    public async Task BlazorSessionDetail_BackLink_CarriesActiveFilters()
    {
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(m =>
            m.Setup(x => x.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(SampleDetail(sessionId)));

        await using (app)
        {
            var html = await (await client.GetAsync(
                    $"/app/sessions/{sessionId}?tenantId=t1&fromUtc=2024-01-01&toUtc=2024-01-31"))
                .Content.ReadAsStringAsync();

            Assert.Contains(
                "/app/dashboard?tenantId=t1&amp;fromUtc=2024-01-01&amp;toUtc=2024-01-31",
                html, StringComparison.Ordinal);
        }
    }

    // ───── AC-109: run progression links carry active filters ──────────────

    [Fact]
    public async Task BlazorSessionDetail_RunProgressionLinks_CarryActiveFilters()
    {
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(m =>
            m.Setup(x => x.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(SampleDetail(sessionId)));

        await using (app)
        {
            var html = await (await client.GetAsync(
                    $"/app/sessions/{sessionId}?tenantId=t1&fromUtc=2024-01-01&toUtc=2024-01-31"))
                .Content.ReadAsStringAsync();

            Assert.Contains(
                $"/app/runs/{Run1Id}?tenantId=t1&amp;fromUtc=2024-01-01&amp;toUtc=2024-01-31",
                html, StringComparison.Ordinal);
            Assert.Contains(
                $"/app/runs/{Run2Id}?tenantId=t1&amp;fromUtc=2024-01-01&amp;toUtc=2024-01-31",
                html, StringComparison.Ordinal);
        }
    }

    // ───── AC-110: links omit filters when absent ───────────────────────

    [Fact]
    public async Task BlazorSessionDetail_Links_OmitFiltersWhenAbsent()
    {
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(m =>
            m.Setup(x => x.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(SampleDetail(sessionId)));

        await using (app)
        {
            var html = await (await client.GetAsync($"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("/app/dashboard?tenantId=t1",      html, StringComparison.Ordinal);
            Assert.Contains($"/app/runs/{Run1Id}?tenantId=t1", html, StringComparison.Ordinal);
            Assert.Contains($"/app/runs/{Run2Id}?tenantId=t1", html, StringComparison.Ordinal);
            Assert.DoesNotContain("fromUtc",                   html, StringComparison.Ordinal);
        }
    }

    // ───── AC-114: back-link and run-links carry status/sort/limit ──────

    [Fact]
    public async Task BlazorSessionDetail_Links_CarryStatusSortLimit()
    {
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(m =>
            m.Setup(x => x.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(SampleDetail(sessionId)));

        await using (app)
        {
            var html = await (await client.GetAsync(
                    $"/app/sessions/{sessionId}?tenantId=t1&status=Completed&sort=desc&limit=50"))
                .Content.ReadAsStringAsync();

            Assert.Contains(
                "/app/dashboard?tenantId=t1&amp;status=Completed&amp;sort=desc&amp;limit=50",
                html, StringComparison.Ordinal);
            Assert.Contains(
                $"/app/runs/{Run1Id}?tenantId=t1&amp;status=Completed&amp;sort=desc&amp;limit=50",
                html, StringComparison.Ordinal);
            Assert.Contains(
                $"/app/runs/{Run2Id}?tenantId=t1&amp;status=Completed&amp;sort=desc&amp;limit=50",
                html, StringComparison.Ordinal);
        }
    }

    // ───── AC-115: oc-session-briefing section renders when Briefing is set ──────

    [Fact]
    public async Task BlazorSessionDetail_Briefing_RendersWhenSet()
    {
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(m =>
            m.Setup(x => x.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(SampleDetail(sessionId)));

        await using (app)
        {
            var html = await (await client.GetAsync(
                    $"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-session-briefing",     html, StringComparison.Ordinal);
                Assert.Contains("Session triage briefing", html, StringComparison.Ordinal);
            Assert.Contains("Worsening",               html, StringComparison.Ordinal);
            Assert.Contains("degrading",               html, StringComparison.Ordinal);
        }
    }

    // ───── AC-116: oc-session-briefing section absent when Briefing is null ──────

    [Fact]
    public async Task BlazorSessionDetail_Briefing_AbsentWhenNull()
    {
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(m =>
        {
            var detail = SampleDetail(sessionId) with { Briefing = null };
            m.Setup(x => x.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(detail);
        });

        await using (app)
        {
            var html = await (await client.GetAsync(
                    $"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-session-briefing", html, StringComparison.Ordinal);
        }
    }

    // ───── AC-117: briefing section never surfaces raw C# property names ─────────

    [Fact]
    public async Task BlazorSessionDetail_Briefing_NoRawPropertyNames()
    {
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(m =>
            m.Setup(x => x.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(SampleDetail(sessionId)));

        await using (app)
        {
            var html = await (await client.GetAsync(
                    $"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            // Must not leak internal C# property names as visible text
            Assert.DoesNotContain("RunCount",            html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("IsIsolated",          html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("StatusPattern",       html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("FingerprintPattern",  html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DominantFingerprint", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SequenceConclusion",  html, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ───── AC-118: isolated run (IsIsolated=true) renders "Yes" for Isolated ─────

    [Fact]
    public async Task BlazorSessionDetail_Briefing_IsolatedRun_ReflectsInHtml()
    {
        var sessionId = Guid.NewGuid();
        var singleRunDetail = new SessionDetailResponse(
            SessionId: sessionId,
            Runs: new[]
            {
                new RecentRunSummary(
                    RunId:            Run1Id,
                    SessionId:        sessionId,
                    Status:           "Completed",
                    AlertFingerprint: "fp-isolated",
                    CreatedAtUtc:     new DateTimeOffset(2025, 4, 2, 10, 0, 0, TimeSpan.Zero),
                    CompletedAtUtc:   new DateTimeOffset(2025, 4, 2, 10, 0, 30, TimeSpan.Zero))
            },
            Briefing: new SessionBriefing(
                RunCount:            1,
                IsIsolated:          true,
                StatusPattern:       "uniform:Completed",
                FingerprintPattern:  "single",
                DominantFingerprint: "fp-isolated",
                SequenceConclusion:  null));

        var (app, client, _) = await CreateBlazorTestHost(m =>
            m.Setup(x => x.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(singleRunDetail));

        await using (app)
        {
            var html = await (await client.GetAsync(
                    $"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-session-briefing", html, StringComparison.Ordinal);
            Assert.Contains("Yes",                 html, StringComparison.Ordinal);
        }
    }

    // ───── AC-119: degrading status pattern is reflected in HTML ─────────────────

    [Fact]
    public async Task BlazorSessionDetail_Briefing_DegradingPattern_ReflectsInHtml()
    {
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(m =>
            m.Setup(x => x.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(SampleDetail(sessionId)));

        await using (app)
        {
            var html = await (await client.GetAsync(
                    $"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-session-briefing", html, StringComparison.Ordinal);
            Assert.Contains("degrading",           html, StringComparison.Ordinal);
            Assert.Contains("mixed",               html, StringComparison.Ordinal);
            Assert.Contains("fp-run1",             html, StringComparison.Ordinal);
        }
    }

    // ───── AC-120: oc-session-recommendations section renders when SessionRecommendations is set ─

    [Fact]
    public async Task BlazorSessionDetail_SessionRecommendations_RendersSection()
    {
        var sessionId = Guid.NewGuid();
        var detail = SampleDetail(sessionId) with
        {
            SessionRecommendations = new[] { new RunRecommendation("ReviewDegradation", "Review run sequence - session is degrading") }
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var html = await (await client.GetAsync($"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-session-recommendations",               html, StringComparison.Ordinal);
            Assert.Contains("What to Inspect Next",                     html, StringComparison.Ordinal);
            Assert.Contains("Review run sequence - session is degrading", html, StringComparison.Ordinal);
        }
    }

    // ───── AC-121: oc-session-recommendations section absent when SessionRecommendations is null ─

    [Fact]
    public async Task BlazorSessionDetail_SessionRecommendations_AbsentWhenNull()
    {
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(sessionId)));  // SessionRecommendations is null by default

        await using (app)
        {
            var html = await (await client.GetAsync($"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-session-recommendations", html, StringComparison.Ordinal);
            Assert.DoesNotContain("What to Inspect Next",        html, StringComparison.Ordinal);
        }
    }

    // ───── AC-122: section absent when both Briefing and SessionRecommendations are null ─

    [Fact]
    public async Task BlazorSessionDetail_SessionRecommendations_AbsentWhenBriefingAndRecsNull()
    {
        var sessionId = Guid.NewGuid();
        var detail = SampleDetail(sessionId) with { Briefing = null, SessionRecommendations = null };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var html = await (await client.GetAsync($"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-session-recommendations", html, StringComparison.Ordinal);
        }
    }

    // ───── AC-123: recommendation shows Instruction text, not machine Key ───────────────

    [Fact]
    public async Task BlazorSessionDetail_SessionRecommendations_ShowsInstructionNotKey()
    {
        var sessionId = Guid.NewGuid();
        var detail = SampleDetail(sessionId) with
        {
            SessionRecommendations = new[] { new RunRecommendation("ReviewDegradation", "Review run sequence - session is degrading") }
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var html = await (await client.GetAsync($"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("Review run sequence - session is degrading", html, StringComparison.Ordinal);
            Assert.DoesNotContain("ReviewDegradation",                    html, StringComparison.Ordinal);
        }
    }

    // ───── AC-124: multiple recommendations each render as oc-recommendation items ──────

    [Fact]
    public async Task BlazorSessionDetail_SessionRecommendations_MultipleItemsRender()
    {
        var sessionId = Guid.NewGuid();
        var detail = SampleDetail(sessionId) with
        {
            SessionRecommendations = new[]
            {
                new RunRecommendation("InspectRepeatedFingerprint", "Inspect recurring alert fingerprint pattern"),
                new RunRecommendation("ReviewDegradation",          "Review run sequence - session is degrading"),
                new RunRecommendation("CompareRuns",                "Compare latest run with the first run of this session")
            }
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var html = await (await client.GetAsync($"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-recommendation",                                        html, StringComparison.Ordinal);
            Assert.Contains("Inspect recurring alert fingerprint pattern",              html, StringComparison.Ordinal);
            Assert.Contains("Review run sequence - session is degrading",               html, StringComparison.Ordinal);
            Assert.Contains("Compare latest run with the first run of this session",    html, StringComparison.Ordinal);
        }
    }

    // ───── AC-125: oc-session-synthesis section renders when SessionSynthesis is set ─────

    [Fact]
    public async Task BlazorSessionDetail_SessionSynthesis_RendersSection()
    {
        var sessionId = Guid.NewGuid();
        var detail = SampleDetail(sessionId) with
        {
            SessionSynthesis = new IncidentSynthesis(
                OverallAssessment: "Session is degrading - runs are worsening",
                FailureMode:       null,
                DataSignal:        null,
                KnowledgeGap:      null,
                ChangeCorrelation: null,
                SessionContext:    null)
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var html = await (await client.GetAsync($"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-session-synthesis",                       html, StringComparison.Ordinal);
            Assert.Contains("Correlated Incident View",                   html, StringComparison.Ordinal);
            Assert.Contains("Session is degrading - runs are worsening",  html, StringComparison.Ordinal);
        }
    }

    // ───── AC-126: oc-session-synthesis section absent when SessionSynthesis is null ──────

    [Fact]
    public async Task BlazorSessionDetail_SessionSynthesis_AbsentWhenNull()
    {
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(sessionId)));

        await using (app)
        {
            var html = await (await client.GetAsync($"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-session-synthesis", html, StringComparison.Ordinal);
        }
    }

    // ───── AC-127: FailureMode field renders when set ────────────────────────────────────

    [Fact]
    public async Task BlazorSessionDetail_SessionSynthesis_FailureModeRenders()
    {
        var sessionId = Guid.NewGuid();
        var detail = SampleDetail(sessionId) with
        {
            SessionSynthesis = new IncidentSynthesis(
                OverallAssessment: "Session is degrading - runs are worsening",
                FailureMode:       "Degrading run status pattern detected",
                DataSignal:        null,
                KnowledgeGap:      null,
                ChangeCorrelation: null,
                SessionContext:    null)
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var html = await (await client.GetAsync($"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("Session is degrading - runs are worsening", html, StringComparison.Ordinal);
            Assert.Contains("Failure Mode",                               html, StringComparison.Ordinal);
            Assert.Contains("Degrading run status pattern detected",      html, StringComparison.Ordinal);
        }
    }

    // ───── AC-128: optional fields absent when null ───────────────────────────────────────

    [Fact]
    public async Task BlazorSessionDetail_SessionSynthesis_OptionalFieldsAbsentWhenNull()
    {
        var sessionId = Guid.NewGuid();
        var detail = SampleDetail(sessionId) with
        {
            SessionSynthesis = new IncidentSynthesis(
                OverallAssessment: "Session runs are stable",
                FailureMode:       null,
                DataSignal:        null,
                KnowledgeGap:      null,
                ChangeCorrelation: null,
                SessionContext:    null)
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var html = await (await client.GetAsync($"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("Failure Mode",    html, StringComparison.Ordinal);
            Assert.DoesNotContain("Data Signal",     html, StringComparison.Ordinal);
            Assert.DoesNotContain("Session Context", html, StringComparison.Ordinal);
        }
    }

    // ───── AC-129: SessionContext field renders when set ─────────────────────────────────

    [Fact]
    public async Task BlazorSessionDetail_SessionSynthesis_SessionContextRenders()
    {
        var sessionId = Guid.NewGuid();
        var detail = SampleDetail(sessionId) with
        {
            SessionSynthesis = new IncidentSynthesis(
                OverallAssessment: "Session runs are stable",
                FailureMode:       null,
                DataSignal:        null,
                KnowledgeGap:      null,
                ChangeCorrelation: null,
                SessionContext:    "Dominant fingerprint: fp-run1")
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetSessionDetailAsync(sessionId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var html = await (await client.GetAsync($"/app/sessions/{sessionId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("Session Context",               html, StringComparison.Ordinal);
            Assert.Contains("Dominant fingerprint: fp-run1", html, StringComparison.Ordinal);
        }
    }
}
