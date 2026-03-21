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
/// AC-83–92: HTTP-level integration tests for the Blazor SSR run-detail page.
/// Each test spins up a lightweight TestServer with the Blazor pipeline and a
/// strict mock of IAgentRunsReportingQueryService, then asserts on the rendered HTML.
/// </summary>
public sealed class AppRunDetailComponentTests
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

        // AppRunDetail.razor injects IAgentRunsReportingQueryService, but the
        // Blazor router also resolves IDashboardQueryService for AppDashboard.razor.
        // Register a permissive stub so the DI container resolves it without errors.
        var dashStub = new Mock<IDashboardQueryService>(MockBehavior.Loose);
        builder.Services.AddSingleton(dashStub.Object);

        var app = builder.Build();
        app.UseAntiforgery();
        app.MapRazorComponents<App>();

        await app.StartAsync();
        return (app, app.GetTestClient(), svc);
    }

    private static RunDetailResponse SampleDetail(Guid runId, Guid? sessionId = null) =>
        new(
            RunId:                runId,
            SessionId:            sessionId,
            Status:               "Completed",
            AlertFingerprint:     "fp-abc",
            CreatedAtUtc:         new DateTimeOffset(2025, 3, 1, 10, 0, 0, TimeSpan.Zero),
            CompletedAtUtc:       new DateTimeOffset(2025, 3, 1, 10, 0, 30, TimeSpan.Zero),
            TotalTokens:          500,
            EstimatedCost:        0.0025m,
            ToolCallCount:        4,
            ToolCallSuccessCount: 3,
            ToolCallFailedCount:  1,
            ActionCount:          2,
            HasCitations:         true,
            Briefing:             SampleBriefing());

    private static RunBriefing SampleBriefing() =>
        new(StatusSeverity:         "ok",
            DurationSeconds:        30.0,
            ToolSuccessRate:        0.75,
            KqlRowCount:            10,
            RunbookHitCount:        2,
            MemoryHitCount:         1,
            DeploymentDiffHitCount: 0,
            KqlCitationCount:       3,
            HasRecommendedActions:  true,
            FailureSignal:          null);

    // ───── AC-83: missing tenantId → 200 + required-panel ───────────────

    [Fact]
    public async Task BlazorRunDetail_MissingTenantId_Returns200WithRequiredMessage()
    {
        var runId = Guid.NewGuid();
        // strict mock with no setup — service must not be called
        var (app, client, _) = await CreateBlazorTestHost(_ => { });
        await using (app)
        {
            var response = await client.GetAsync($"/app/runs/{runId}");
            var body     = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Tenant ID Required", body, StringComparison.Ordinal);
            Assert.Contains("oc-error-panel",     body, StringComparison.Ordinal);
        }
    }

    // ───── AC-84: valid tenantId + runId → service called with correct args ─

    [Fact]
    public async Task BlazorRunDetail_ValidArgs_ServiceCalledWithCorrectIds()
    {
        var runId = Guid.NewGuid();
        var (app, client, svc) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "tenant-abc", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(runId)));

        await using (app)
        {
            await client.GetAsync($"/app/runs/{runId}?tenantId=tenant-abc");

            svc.Verify(s => s.GetRunDetailAsync(runId, "tenant-abc", It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    // ───── AC-85: run detail rendered — metadata fields ──────────────────

    [Fact]
    public async Task BlazorRunDetail_ValidRun_RendersMetadataFields()
    {
        var runId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(runId)));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains(runId.ToString(), body,          StringComparison.Ordinal);
            Assert.Contains("Completed",      body,          StringComparison.Ordinal);
            Assert.Contains("fp-abc",         body,          StringComparison.Ordinal);
            Assert.Contains("2025-03-01",     body,          StringComparison.Ordinal);
            Assert.Contains("500",            body,          StringComparison.Ordinal);
            Assert.Contains("0.0025",         body,          StringComparison.Ordinal);
            Assert.Contains("30.0s",          body,          StringComparison.Ordinal); // duration
        }
    }

    // ───── AC-86: tool-call summary section rendered ──────────────────────

    [Fact]
    public async Task BlazorRunDetail_ValidRun_RendersToolCallSummary()
    {
        var runId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(runId)));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-tool-call-summary", body, StringComparison.Ordinal);
            Assert.Contains("Tool Call Summary",     body, StringComparison.Ordinal);
        }
    }

    // ───── AC-87: evidence summary section rendered ───────────────────────

    [Fact]
    public async Task BlazorRunDetail_ValidRun_RendersEvidenceSummary()
    {
        var runId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(runId)));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-evidence-summary", body, StringComparison.Ordinal);
            Assert.Contains("Evidence Summary",    body, StringComparison.Ordinal);
            Assert.Contains("Has Citations",       body, StringComparison.Ordinal);
            Assert.Contains("Yes",                 body, StringComparison.Ordinal);
            Assert.Contains("Action Count",        body, StringComparison.Ordinal);
        }
    }

    // ───── AC-88: service returns null → not-found panel ─────────────────

    [Fact]
    public async Task BlazorRunDetail_NotFound_RendersNotFoundPanel()
    {
        var runId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync((RunDetailResponse?)null));

        await using (app)
        {
            var response = await client.GetAsync($"/app/runs/{runId}?tenantId=t1");
            var body     = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Run Not Found",       body, StringComparison.Ordinal);
            Assert.Contains("oc-not-found-panel",  body, StringComparison.Ordinal);
        }
    }

    // ───── AC-89: service throws → error panel, no exception details ──────

    [Fact]
    public async Task BlazorRunDetail_ServiceThrows_RendersErrorPanelWithNoLeakedDetails()
    {
        var runId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("internal-secret")));

        await using (app)
        {
            var response = await client.GetAsync($"/app/runs/{runId}?tenantId=t1");
            var body     = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Run Detail Unavailable",   body, StringComparison.Ordinal);
            Assert.Contains("oc-error-panel",           body, StringComparison.Ordinal);
            Assert.DoesNotContain("internal-secret",        body, StringComparison.Ordinal);
            Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
            Assert.DoesNotContain("StackTrace",         body, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ───── AC-90: no POST form in rendered output ─────────────────────────

    [Fact]
    public async Task BlazorRunDetail_NoPostFormInOutput()
    {
        var runId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(runId)));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("method=\"post\"", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ───── AC-91: session link rendered when sessionId present ───────────

    [Fact]
    public async Task BlazorRunDetail_WithSessionId_RendersSessionLink()
    {
        var runId     = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(runId, sessionId)));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains($"/app/sessions/{sessionId}?tenantId=t1", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-92: cross-tenant isolation ─────────────────────────────────

    [Fact]
    public async Task BlazorRunDetail_CrossTenant_ServiceCalledWithDistinctTenantIds()
    {
        var runId = Guid.NewGuid();
        var (app, client, svc) = await CreateBlazorTestHost(mock =>
        {
            mock.Setup(s => s.GetRunDetailAsync(runId, "tenant-a", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(runId));
            mock.Setup(s => s.GetRunDetailAsync(runId, "tenant-b", It.IsAny<CancellationToken>()))
                .ReturnsAsync((RunDetailResponse?)null);
        });

        await using (app)
        {
            await client.GetAsync($"/app/runs/{runId}?tenantId=tenant-a");
            await client.GetAsync($"/app/runs/{runId}?tenantId=tenant-b");

            svc.Verify(s => s.GetRunDetailAsync(runId, "tenant-a", It.IsAny<CancellationToken>()), Times.Once);
            svc.Verify(s => s.GetRunDetailAsync(runId, "tenant-b", It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    // ───── AC-105: back link carries active filters ─────────────────────

    [Fact]
    public async Task BlazorRunDetail_BackLink_CarriesActiveFilters()
    {
        var runId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(runId)));

        await using (app)
        {
            var body = await (await client.GetAsync(
                    $"/app/runs/{runId}?tenantId=t1&fromUtc=2024-01-01&toUtc=2024-01-31"))
                .Content.ReadAsStringAsync();

            Assert.Contains(
                "/app/dashboard?tenantId=t1&amp;fromUtc=2024-01-01&amp;toUtc=2024-01-31",
                body, StringComparison.Ordinal);
        }
    }

    // ───── AC-106: session link carries active filters ────────────────────

    [Fact]
    public async Task BlazorRunDetail_SessionLink_CarriesActiveFilters()
    {
        var runId     = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(runId, sessionId)));

        await using (app)
        {
            var body = await (await client.GetAsync(
                    $"/app/runs/{runId}?tenantId=t1&fromUtc=2024-01-01&toUtc=2024-01-31"))
                .Content.ReadAsStringAsync();

            Assert.Contains(
                $"/app/sessions/{sessionId}?tenantId=t1&amp;fromUtc=2024-01-01&amp;toUtc=2024-01-31",
                body, StringComparison.Ordinal);
        }
    }

    // ───── AC-107: links omit filters when absent ───────────────────────

    [Fact]
    public async Task BlazorRunDetail_Links_OmitFiltersWhenAbsent()
    {
        var runId     = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(runId, sessionId)));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("/app/dashboard?tenantId=t1",             body, StringComparison.Ordinal);
            Assert.Contains($"/app/sessions/{sessionId}?tenantId=t1", body, StringComparison.Ordinal);
            Assert.DoesNotContain("fromUtc",                          body, StringComparison.Ordinal);
        }
    }

    // ───── AC-93: briefing section renders when Briefing is set ──────────

    [Fact]
    public async Task BlazorRunDetail_WithBriefing_RendersBriefingSection()
    {
        var runId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(runId)));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-triage-briefing",   body, StringComparison.Ordinal);
            Assert.Contains("Triage Briefing",       body, StringComparison.Ordinal);
            Assert.Contains("OK",                    body, StringComparison.Ordinal); // severity uppercased
            Assert.Contains("75%",                   body, StringComparison.Ordinal); // tool success rate
        }
    }

    // ───── AC-94: briefing section absent when Briefing is null ─────────

    [Fact]
    public async Task BlazorRunDetail_NullBriefing_DoesNotRenderBriefingSection()
    {
        var runId = Guid.NewGuid();
        var detail = SampleDetail(runId) with { Briefing = null };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-triage-briefing", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-95: raw JSON keys must never appear in rendered HTML ───────

    [Fact]
    public async Task BlazorRunDetail_BriefingPresent_NoRawJsonInBody()
    {
        var runId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(runId)));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("rowCount",      body, StringComparison.Ordinal);
            Assert.DoesNotContain("runbookHits",   body, StringComparison.Ordinal);
            Assert.DoesNotContain("citationsJson", body, StringComparison.Ordinal);
            Assert.DoesNotContain("summaryJson",   body, StringComparison.Ordinal);
        }
    }

    // ───── AC-113: back-link and session-link carry status/sort/limit ───

    [Fact]
    public async Task BlazorRunDetail_Links_CarryStatusSortLimit()
    {
        var runId     = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(runId, sessionId)));

        await using (app)
        {
            var body = await (await client.GetAsync(
                    $"/app/runs/{runId}?tenantId=t1&status=Completed&sort=desc&limit=50"))
                .Content.ReadAsStringAsync();

            Assert.Contains(
                "/app/dashboard?tenantId=t1&amp;status=Completed&amp;sort=desc&amp;limit=50",
                body, StringComparison.Ordinal);
            Assert.Contains(
                $"/app/sessions/{sessionId}?tenantId=t1&amp;status=Completed&amp;sort=desc&amp;limit=50",
                body, StringComparison.Ordinal);
        }
    }

    // ───── AC-114: oc-run-recommendations section renders when RunRecommendations is set ─

    [Fact]
    public async Task BlazorRunDetail_RunRecommendations_RendersSection()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            RunRecommendations = new[] { new RunRecommendation("InspectToolCalls", "Inspect failed tool calls for root cause") }
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-run-recommendations",                       body, StringComparison.Ordinal);
            Assert.Contains("What to Inspect Next",                         body, StringComparison.Ordinal);
            Assert.Contains("Inspect failed tool calls for root cause",     body, StringComparison.Ordinal);
        }
    }

    // ───── AC-115: oc-run-recommendations section absent when RunRecommendations is null ─

    [Fact]
    public async Task BlazorRunDetail_RunRecommendations_AbsentWhenNull()
    {
        var runId = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(runId)));  // RunRecommendations is null by default

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-run-recommendations", body, StringComparison.Ordinal);
            Assert.DoesNotContain("What to Inspect Next",   body, StringComparison.Ordinal);
        }
    }

    // ───── AC-116: section absent when both Briefing and RunRecommendations are null ─────

    [Fact]
    public async Task BlazorRunDetail_RunRecommendations_AbsentWhenBriefingAndRecsNull()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { Briefing = null, RunRecommendations = null };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-run-recommendations", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-117: recommendation shows Instruction text, not machine Key ───────────────

    [Fact]
    public async Task BlazorRunDetail_RunRecommendations_ShowsInstructionNotKey()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            RunRecommendations = new[] { new RunRecommendation("InspectToolCalls", "Inspect failed tool calls for root cause") }
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("Inspect failed tool calls for root cause", body, StringComparison.Ordinal);
            Assert.DoesNotContain("InspectToolCalls",                   body, StringComparison.Ordinal);
        }
    }

    // ───── AC-118: multiple recommendations each render as oc-recommendation items ──────

    [Fact]
    public async Task BlazorRunDetail_RunRecommendations_MultipleItemsRender()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            RunRecommendations = new[]
            {
                new RunRecommendation("CheckKqlData",       "Inspect KQL query — no data rows were returned"),
                new RunRecommendation("CheckRunbook",       "Review runbook coverage for this alert type"),
                new RunRecommendation("CompareSessionRuns", "Compare this run with other runs in the same session")
            }
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-recommendation",                                        body, StringComparison.Ordinal);
            Assert.Contains("Inspect KQL query",                                        body, StringComparison.Ordinal);
            Assert.Contains("Review runbook coverage",                                  body, StringComparison.Ordinal);
            Assert.Contains("Compare this run with other runs in the same session",     body, StringComparison.Ordinal);
        }
    }

    // ───── AC-119: oc-incident-synthesis section renders when Synthesis is set ──────────

    [Fact]
    public async Task BlazorRunDetail_IncidentSynthesis_RendersSection()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            Synthesis = new IncidentSynthesis(
                OverallAssessment: "Run completed successfully",
                FailureMode:       null,
                DataSignal:        null,
                KnowledgeGap:      null,
                ChangeCorrelation: null,
                SessionContext:    null)
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-incident-synthesis",        body, StringComparison.Ordinal);
            Assert.Contains("Correlated Incident View",     body, StringComparison.Ordinal);
            Assert.Contains("Run completed successfully",   body, StringComparison.Ordinal);
        }
    }

    // ───── AC-120: oc-incident-synthesis section absent when Synthesis is null ───────────

    [Fact]
    public async Task BlazorRunDetail_IncidentSynthesis_AbsentWhenNull()
    {
        var runId  = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(runId)));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-incident-synthesis", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-121: FailureMode field renders when set ────────────────────────────────────

    [Fact]
    public async Task BlazorRunDetail_IncidentSynthesis_FailureModeRenders()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            Synthesis = new IncidentSynthesis(
                OverallAssessment: "Run failed - tool execution error",
                FailureMode:       "Tool call failures caused run failure",
                DataSignal:        null,
                KnowledgeGap:      null,
                ChangeCorrelation: null,
                SessionContext:    null)
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("Run failed - tool execution error",     body, StringComparison.Ordinal);
            Assert.Contains("Failure Mode",                          body, StringComparison.Ordinal);
            Assert.Contains("Tool call failures caused run failure", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-122: optional fields absent when null ───────────────────────────────────────

    [Fact]
    public async Task BlazorRunDetail_IncidentSynthesis_OptionalFieldsAbsentWhenNull()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            Synthesis = new IncidentSynthesis(
                OverallAssessment: "Run completed successfully",
                FailureMode:       null,
                DataSignal:        null,
                KnowledgeGap:      null,
                ChangeCorrelation: null,
                SessionContext:    null)
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("Failure Mode",       body, StringComparison.Ordinal);
            Assert.DoesNotContain("Data Signal",        body, StringComparison.Ordinal);
            Assert.DoesNotContain("Knowledge Gap",      body, StringComparison.Ordinal);
            Assert.DoesNotContain("Change Correlation", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Session Context",    body, StringComparison.Ordinal);
        }
    }

    // ───── AC-123: SessionContext field renders when set ─────────────────────────────────

    [Fact]
    public async Task BlazorRunDetail_IncidentSynthesis_SessionContextRenders()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            Synthesis = new IncidentSynthesis(
                OverallAssessment: "Run completed successfully",
                FailureMode:       null,
                DataSignal:        null,
                KnowledgeGap:      null,
                ChangeCorrelation: null,
                SessionContext:    "Run is part of a multi-run session")
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("Session Context",                    body, StringComparison.Ordinal);
            Assert.Contains("Run is part of a multi-run session", body, StringComparison.Ordinal);
        }
    }

    // ───── Slice 90 helpers ───────────────────────────────────────────────

    private static SimilarPriorIncident SampleHit() =>
        new(PriorRunId:       Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            AlertFingerprint: "fp-prior-1",
            SummarySnippet:   "Database connection pool exhausted during peak load",
            Score:            0.87,
            OccurredAtUtc:    new DateTimeOffset(2025, 1, 15, 9, 0, 0, TimeSpan.Zero));

    // ───── AC-124: oc-similar-incidents section renders when populated ────

    [Fact]
    public async Task BlazorRunDetail_SimilarIncidents_SectionRendersWhenPopulated()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            SimilarPriorIncidents = new[] { SampleHit() }
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-similar-incidents", body, StringComparison.Ordinal);
            Assert.Contains("Prior Similar Incidents", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-125: oc-similar-incidents section absent when null ──────────

    [Fact]
    public async Task BlazorRunDetail_SimilarIncidents_SectionAbsentWhenNull()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { SimilarPriorIncidents = null };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-similar-incidents", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-126: oc-similar-incidents section absent when empty ─────────

    [Fact]
    public async Task BlazorRunDetail_SimilarIncidents_SectionAbsentWhenEmpty()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            SimilarPriorIncidents = Array.Empty<SimilarPriorIncident>()
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-similar-incidents", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-127: item fields render correctly ───────────────────────────

    [Fact]
    public async Task BlazorRunDetail_SimilarIncidents_ItemFieldsRender()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            SimilarPriorIncidents = new[] { SampleHit() }
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("87%",                                                  body, StringComparison.Ordinal);
            Assert.Contains("fp-prior-1",                                            body, StringComparison.Ordinal);
            Assert.Contains("Database connection pool exhausted during peak load",   body, StringComparison.Ordinal);
            Assert.Contains("2025-01-15",                                            body, StringComparison.Ordinal);
        }
    }

    // ───── AC-128: two hits render two list items ─────────────────────────

    [Fact]
    public async Task BlazorRunDetail_SimilarIncidents_TwoHitsRenderTwoItems()
    {
        var runId   = Guid.NewGuid();
        var second  = SampleHit() with { PriorRunId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002") };
        var detail  = SampleDetail(runId) with
        {
            SimilarPriorIncidents = new[] { SampleHit(), second }
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body  = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();
            var count = System.Text.RegularExpressions.Regex.Matches(body, "oc-similar-incident\"").Count;

            Assert.Equal(2, count);
        }
    }

    // ───── Slice 91: Service Bus triage signals ───────────────────────────

    private static ServiceBusSignals SampleSbSignals() =>
        new(TotalQueues:             1,
            TotalActiveMessages:     12,
            TotalDeadLetterMessages: 3,
            Queues: [new ServiceBusQueueSignal("ops-alerts", 12, 3, "warning")]);

    // ───── AC-130: oc-service-bus-signals section renders when queues populated

    [Fact]
    public async Task BlazorRunDetail_ServiceBusSignals_SectionRendersWhenQueuesPopulated()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { ServiceBusSignals = SampleSbSignals() };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-service-bus-signals", body, StringComparison.Ordinal);
            Assert.Contains("Service Bus Signals",     body, StringComparison.Ordinal);
        }
    }

    // ───── AC-131: oc-service-bus-signals section absent when null ────────

    [Fact]
    public async Task BlazorRunDetail_ServiceBusSignals_SectionAbsentWhenNull()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { ServiceBusSignals = null };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-service-bus-signals", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-132: oc-service-bus-signals section absent when queues empty ─

    [Fact]
    public async Task BlazorRunDetail_ServiceBusSignals_SectionAbsentWhenQueuesEmpty()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            ServiceBusSignals = new ServiceBusSignals(0, 0, 0, Array.Empty<ServiceBusQueueSignal>())
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-service-bus-signals", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-133: queue fields render correctly ──────────────────────────

    [Fact]
    public async Task BlazorRunDetail_ServiceBusSignals_QueueFieldsRender()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { ServiceBusSignals = SampleSbSignals() };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("ops-alerts",      body, StringComparison.Ordinal);
            Assert.Contains("warning",         body, StringComparison.Ordinal);
            Assert.Contains(">12<",             body, StringComparison.Ordinal);
            Assert.Contains(">3<",              body, StringComparison.Ordinal);
        }
    }

    // ───── Slice 93: Azure change signals ────────────────────────────────

    private static AzureChangeSynthesis SampleAzureSignals() =>
        new(TotalDeployments: 1,
            Deployments: [new AzureDeploymentSignal(
                DeploymentName:    "my-deployment",
                Timestamp:         new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero),
                ProvisioningState: "Succeeded",
                ResourceGroup:     "my-rg")]);

    // ───── AC-145: oc-azure-change-signals section renders when deployments present ─

    [Fact]
    public async Task BlazorRunDetail_AzureChangeSignals_SectionRendersWhenDeploymentsPresent()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { AzureChangeSynthesis = SampleAzureSignals() };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-azure-change-signals", body, StringComparison.Ordinal);
            Assert.Contains("Azure Change Signals",    body, StringComparison.Ordinal);
        }
    }

    // ───── AC-146: oc-azure-change-signals section absent when null ───────

    [Fact]
    public async Task BlazorRunDetail_AzureChangeSignals_SectionAbsentWhenNull()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { AzureChangeSynthesis = null };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-azure-change-signals", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-147: oc-azure-change-signals section absent when deployments empty ──

    [Fact]
    public async Task BlazorRunDetail_AzureChangeSignals_SectionAbsentWhenDeploymentsEmpty()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            AzureChangeSynthesis = new AzureChangeSynthesis(0, Array.Empty<AzureDeploymentSignal>())
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-azure-change-signals", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-148: deployment name, state, resource group render correctly ──

    [Fact]
    public async Task BlazorRunDetail_AzureChangeSignals_DeploymentFieldsRender()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { AzureChangeSynthesis = SampleAzureSignals() };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("my-deployment", body, StringComparison.Ordinal);
            Assert.Contains("Succeeded",     body, StringComparison.Ordinal);
            Assert.Contains("my-rg",         body, StringComparison.Ordinal);
        }
    }

    // ───── AC-149: timestamp renders when present, absent when null ───────

    [Fact]
    public async Task BlazorRunDetail_AzureChangeSignals_TimestampRendersWhenPresent()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { AzureChangeSynthesis = SampleAzureSignals() };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            // SampleAzureSignals has Timestamp = 2024-06-01
            Assert.Contains("2024-06-01",              body, StringComparison.Ordinal);
            Assert.Contains("oc-deployment-timestamp", body, StringComparison.Ordinal);
        }
    }

    // ───── Slice 94: Connectivity signals ────────────────────────────────

    private static ConnectivitySignals SampleConnectivitySignals() =>
        new(TotalSignals: 1,
            Signals: [new ConnectivitySignal(
                Category: "dns",
                Summary:  "DNS resolution failure detected")]);

    // ───── AC-150: oc-connectivity-signals section renders when signals present ─

    [Fact]
    public async Task BlazorRunDetail_ConnectivitySignals_SectionRendersWhenSignalsPresent()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { ConnectivitySignals = SampleConnectivitySignals() };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-connectivity-signals", body, StringComparison.Ordinal);
            Assert.Contains("Connectivity Signals",    body, StringComparison.Ordinal);
        }
    }

    // ───── AC-151: oc-connectivity-signals section absent when null ───────

    [Fact]
    public async Task BlazorRunDetail_ConnectivitySignals_SectionAbsentWhenNull()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { ConnectivitySignals = null };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-connectivity-signals", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-152: oc-connectivity-signals section absent when TotalSignals is zero ──

    [Fact]
    public async Task BlazorRunDetail_ConnectivitySignals_SectionAbsentWhenTotalSignalsZero()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            ConnectivitySignals = new ConnectivitySignals(0, Array.Empty<ConnectivitySignal>())
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-connectivity-signals", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-153: oc-connectivity-signal items render per signal ─────────

    [Fact]
    public async Task BlazorRunDetail_ConnectivitySignals_SignalItemsRender()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { ConnectivitySignals = SampleConnectivitySignals() };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-connectivity-signal", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-154: category and summary render correctly ──────────────────

    [Fact]
    public async Task BlazorRunDetail_ConnectivitySignals_CategoryAndSummaryRender()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { ConnectivitySignals = SampleConnectivitySignals() };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-signal-category",            body, StringComparison.Ordinal);
            Assert.Contains("dns",                           body, StringComparison.Ordinal);
            Assert.Contains("oc-signal-summary",             body, StringComparison.Ordinal);
            Assert.Contains("DNS resolution failure detected", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-155: multiple signals all render ────────────────────────────

    [Fact]
    public async Task BlazorRunDetail_ConnectivitySignals_MultipleSignalsAllRender()
    {
        var runId  = Guid.NewGuid();
        var signals = new ConnectivitySignals(2,
        [
            new ConnectivitySignal("timeout", "Network timeout detected"),
            new ConnectivitySignal("tls",     "TLS/SSL error detected"),
        ]);
        var detail = SampleDetail(runId) with { ConnectivitySignals = signals };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("Network timeout detected", body, StringComparison.Ordinal);
            Assert.Contains("TLS/SSL error detected",   body, StringComparison.Ordinal);
        }
    }

    // ───── AC-156: connectivity section is absent when existing run has no signals ──

    [Fact]
    public async Task BlazorRunDetail_ConnectivitySignals_SectionAbsentForNormalRun()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId); // ConnectivitySignals defaults to null
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-connectivity-signals", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-157: page returns 200 OK when ConnectivitySignals null ────────

    [Fact]
    public async Task BlazorRunDetail_ConnectivitySignals_PageReturnsOkWhenNull()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { ConnectivitySignals = null };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var response = await client.GetAsync($"/app/runs/{runId}?tenantId=t1");
            Assert.True(response.IsSuccessStatusCode);
        }
    }

    // ───── AC-158: refused category renders correctly ────────────────────

    [Fact]
    public async Task BlazorRunDetail_ConnectivitySignals_RefusedCategoryRenders()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            ConnectivitySignals = new ConnectivitySignals(1,
                [new ConnectivitySignal("refused", "Connection refused by remote endpoint")])
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("refused",                              body, StringComparison.Ordinal);
            Assert.Contains("Connection refused by remote endpoint", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-159: gateway-path category renders correctly ────────────────

    [Fact]
    public async Task BlazorRunDetail_ConnectivitySignals_GatewayPathCategoryRenders()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            ConnectivitySignals = new ConnectivitySignals(1,
                [new ConnectivitySignal("gateway-path", "Gateway or upstream proxy issue detected")])
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("gateway-path",                          body, StringComparison.Ordinal);
            Assert.Contains("Gateway or upstream proxy issue detected", body, StringComparison.Ordinal);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Slice 95 — Identity / Auth Failure Synthesis (AC-160 – AC-169)
    // ═══════════════════════════════════════════════════════════════════════

    private static AuthSignals SampleAuthSignals() =>
        new(TotalSignals: 1,
            Signals: [new AuthSignal(
                Category: "unauthorized",
                Summary:  "Authentication failure or invalid credentials detected")]);

    // ───── AC-160: oc-auth-signals section renders when signals present ───

    [Fact]
    public async Task BlazorRunDetail_AuthSignals_SectionRendersWhenSignalsPresent()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { AuthSignals = SampleAuthSignals() };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-auth-signals", body, StringComparison.Ordinal);
            Assert.Contains("Auth Signals",    body, StringComparison.Ordinal);
        }
    }

    // ───── AC-161: oc-auth-signals section absent when null ──────────────

    [Fact]
    public async Task BlazorRunDetail_AuthSignals_SectionAbsentWhenNull()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { AuthSignals = null };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-auth-signals", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-162: oc-auth-signals section absent when TotalSignals is zero ──

    [Fact]
    public async Task BlazorRunDetail_AuthSignals_SectionAbsentWhenTotalSignalsZero()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            AuthSignals = new AuthSignals(TotalSignals: 0, Signals: [])
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-auth-signals", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-163: individual signal items render ─────────────────────────

    [Fact]
    public async Task BlazorRunDetail_AuthSignals_IndividualItemsRender()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { AuthSignals = SampleAuthSignals() };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-auth-signal", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-164: category and summary render correctly ──────────────────

    [Fact]
    public async Task BlazorRunDetail_AuthSignals_CategoryAndSummaryRender()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { AuthSignals = SampleAuthSignals() };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-auth-category",                                    body, StringComparison.Ordinal);
            Assert.Contains("unauthorized",                                         body, StringComparison.Ordinal);
            Assert.Contains("oc-auth-summary",                                     body, StringComparison.Ordinal);
            Assert.Contains("Authentication failure or invalid credentials detected", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-165: multiple signals all render ────────────────────────────

    [Fact]
    public async Task BlazorRunDetail_AuthSignals_MultipleSignalsAllRender()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            AuthSignals = new AuthSignals(TotalSignals: 2, Signals:
            [
                new AuthSignal("unauthorized",    "Authentication failure or invalid credentials detected"),
                new AuthSignal("managed-identity", "Managed identity token acquisition failure detected")
            ])
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("unauthorized",     body, StringComparison.Ordinal);
            Assert.Contains("managed-identity", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-166: auth-signals section absent on normal run ─────────────

    [Fact]
    public async Task BlazorRunDetail_AuthSignals_SectionAbsentOnNormalRun()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId); // AuthSignals defaults to null
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-auth-signals", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-167: 200 OK when AuthSignals is null ────────────────────────

    [Fact]
    public async Task BlazorRunDetail_AuthSignals_Returns200WhenNull()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { AuthSignals = null };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var response = await client.GetAsync($"/app/runs/{runId}?tenantId=t1");
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ───── AC-168: forbidden category renders correctly ───────────────────

    [Fact]
    public async Task BlazorRunDetail_AuthSignals_ForbiddenCategoryRenders()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            AuthSignals = new AuthSignals(1,
                [new AuthSignal("forbidden", "Authorisation denied or insufficient permissions")])
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("forbidden",                                    body, StringComparison.Ordinal);
            Assert.Contains("Authorisation denied or insufficient permissions", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-169: managed-identity category renders correctly ────────────

    [Fact]
    public async Task BlazorRunDetail_AuthSignals_ManagedIdentityCategoryRenders()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            AuthSignals = new AuthSignals(1,
                [new AuthSignal("managed-identity", "Managed identity token acquisition failure detected")])
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("managed-identity",                                body, StringComparison.Ordinal);
            Assert.Contains("Managed identity token acquisition failure detected", body, StringComparison.Ordinal);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Slice 96 — Unified Incident Workspace (AC-170 – AC-179)
    // ═══════════════════════════════════════════════════════════════════════

    // ───── AC-170: oc-incident-workspace outer section renders when detail loaded ─

    [Fact]
    public async Task BlazorRunDetail_IncidentWorkspace_PresentWhenLoaded()
    {
        var runId  = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(runId)));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-incident-workspace", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-171: oc-incident-overview renders when Briefing is set ──────

    [Fact]
    public async Task BlazorRunDetail_IncidentOverview_RendersWhenBriefingSet()
    {
        var runId  = Guid.NewGuid();
        // SampleDetail has Briefing = SampleBriefing() by default
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(runId)));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-incident-overview", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-172: oc-incident-overview renders when only Synthesis is set ─

    [Fact]
    public async Task BlazorRunDetail_IncidentOverview_RendersWhenSynthesisOnly()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            Briefing  = null,
            Synthesis = new IncidentSynthesis(
                OverallAssessment: "Synthesis-only run",
                FailureMode:       null,
                DataSignal:        null,
                KnowledgeGap:      null,
                ChangeCorrelation: null,
                SessionContext:    null)
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-incident-overview", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-173: oc-incident-overview absent when both Briefing and Synthesis null ─

    [Fact]
    public async Task BlazorRunDetail_IncidentOverview_AbsentWhenBothNull()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { Briefing = null };
        // Synthesis is null by default in SampleDetail
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-incident-overview", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-174: oc-signals-band renders when ServiceBusSignals present ─

    [Fact]
    public async Task BlazorRunDetail_SignalsBand_RendersWhenServiceBusPresent()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            ServiceBusSignals = SampleSbSignals()
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-signals-band", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-175: oc-signals-band renders when only AuthSignals present ──

    [Fact]
    public async Task BlazorRunDetail_SignalsBand_RendersWhenAuthPresent()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            AuthSignals = SampleAuthSignals()
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-signals-band", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-176: oc-signals-band absent when all signal fields null ─────

    [Fact]
    public async Task BlazorRunDetail_SignalsBand_AbsentWhenAllSignalsNull()
    {
        var runId  = Guid.NewGuid();
        // SampleDetail has no signals set by default
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(runId)));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-signals-band", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-177: oc-related-context renders when SimilarPriorIncidents set ─

    [Fact]
    public async Task BlazorRunDetail_RelatedContext_RendersWhenSimilarIncidentsPresent()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            SimilarPriorIncidents = new[] { SampleHit() }
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-related-context", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-178: oc-next-steps renders when RunRecommendations present ──

    [Fact]
    public async Task BlazorRunDetail_NextSteps_RendersWhenRecommendationsPresent()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            RunRecommendations = new[] { new RunRecommendation("InspectToolCalls", "Inspect failed tool calls for root cause") }
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-next-steps", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-179: oc-incident-workspace absent when detail not found ─────

    [Fact]
    public async Task BlazorRunDetail_WorkspaceOuter_AbsentOnNotFound()
    {
        var runId  = Guid.NewGuid();
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync((RunDetailResponse?)null));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-incident-workspace", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-180: proposed-actions section absent when ProposedNextActions is null ─────

    [Fact]
    public async Task BlazorRunDetail_ProposedActions_AbsentWhenNull()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { ProposedNextActions = null };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-proposed-actions", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-181: Auth proposal text and CSS category class rendered ─────

    [Fact]
    public async Task BlazorRunDetail_ProposedActions_AuthProposalRendered()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            ProposedNextActions = new List<ProposedNextAction>
            {
                new("Investigate authentication failure", "Auth signal detected", "Auth"),
            }
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("Investigate authentication failure", body, StringComparison.Ordinal);
            Assert.Contains("oc-proposed-auth", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-182: ServiceBus dead-letter proposal text and CSS category class rendered ─────

    [Fact]
    public async Task BlazorRunDetail_ProposedActions_ServiceBusProposalRendered()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            ProposedNextActions = new List<ProposedNextAction>
            {
                new("Review dead-letter queue orders (3 unprocessed messages)", "Dead-letter count: 3", "ServiceBus"),
            }
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("Review dead-letter queue orders", body, StringComparison.Ordinal);
            Assert.Contains("oc-proposed-servicebus", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-183: Connectivity proposal text and CSS category class rendered ─────

    [Fact]
    public async Task BlazorRunDetail_ProposedActions_ConnectivityProposalRendered()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            ProposedNextActions = new List<ProposedNextAction>
            {
                new("Check network connectivity dns failure detected", "Connectivity signal", "Connectivity"),
            }
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("dns failure detected", body, StringComparison.Ordinal);
            Assert.Contains("oc-proposed-connectivity", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-184: AzureChange failed-deployment proposal text and CSS category class rendered ─────

    [Fact]
    public async Task BlazorRunDetail_ProposedActions_AzureChangeProposalRendered()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            ProposedNextActions = new List<ProposedNextAction>
            {
                new("Review failed deployment infra in rg-prod", "Provisioning state: Failed", "AzureChange"),
            }
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("Review failed deployment infra", body, StringComparison.Ordinal);
            Assert.Contains("oc-proposed-azurechange", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-185: Briefing failure-signal proposal text rendered ─────

    [Fact]
    public async Task BlazorRunDetail_ProposedActions_BriefingFailureSignalProposalRendered()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            ProposedNextActions = new List<ProposedNextAction>
            {
                new("Investigate run failure signal: RunFailed", "Failure signal in briefing", "Briefing"),
            }
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("Investigate run failure signal", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-186: Briefing high-tool-failure-rate proposal text rendered ─────

    [Fact]
    public async Task BlazorRunDetail_ProposedActions_BriefingToolRateProposalRendered()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            ProposedNextActions = new List<ProposedNextAction>
            {
                new("Audit tool configuration high tool failure rate detected", "Tool success rate is 25%", "Briefing"),
            }
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("Audit tool configuration", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-187: multiple proposals all rendered ─────

    [Fact]
    public async Task BlazorRunDetail_ProposedActions_MultipleProposalsAllRendered()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            ProposedNextActions = new List<ProposedNextAction>
            {
                new("Investigate authentication failure", "Auth signal", "Auth"),
                new("Review dead-letter queue orders (5 messages)", "Dead-letter count", "ServiceBus"),
            }
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("Investigate authentication failure", body, StringComparison.Ordinal);
            Assert.Contains("Review dead-letter queue orders", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-188: proposed-actions section present with aria-label when proposals exist ─────

    [Fact]
    public async Task BlazorRunDetail_ProposedActions_SectionHasAriaLabel()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            ProposedNextActions = new List<ProposedNextAction>
            {
                new("Check connectivity failure detected", "Connectivity signal", "Connectivity"),
            }
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-proposed-actions", body, StringComparison.Ordinal);
            Assert.Contains("aria-label=\"Proposed next actions\"", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-189: proposed-actions section absent when list is empty ─────

    [Fact]
    public async Task BlazorRunDetail_ProposedActions_AbsentWhenEmptyList()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with
        {
            ProposedNextActions = new List<ProposedNextAction>()
        };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-proposed-actions", body, StringComparison.Ordinal);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Slice 98 — Evidence Quality Assessment (AC-190 – AC-193)
    // ═══════════════════════════════════════════════════════════════════════

    private static EvidenceQualityAssessment SampleEvidenceQuality(
        EvidenceStrength strength = EvidenceStrength.Strong,
        IReadOnlyList<string>? missingAreas = null) =>
        new(strength,
            strength == EvidenceStrength.Strong ? EvidenceCompleteness.Complete : EvidenceCompleteness.Partial,
            strength == EvidenceStrength.Strong ? 8 : 4,
            8,
            missingAreas ?? [],
            "Evidence is comprehensive. Proceed with confidence.",
            DateTimeOffset.UtcNow);

    // ───── AC-190: oc-evidence-quality renders when EvidenceQuality present ─

    [Fact]
    public async Task BlazorRunDetail_EvidenceQuality_RendersWhenPresent()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { EvidenceQuality = SampleEvidenceQuality() };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-evidence-quality", body, StringComparison.Ordinal);
            Assert.Contains("oc-eq-strong",         body, StringComparison.Ordinal);
        }
    }

    // ───── AC-191: oc-evidence-quality absent when EvidenceQuality null ─────

    [Fact]
    public async Task BlazorRunDetail_EvidenceQuality_AbsentWhenNull()
    {
        var runId = Guid.NewGuid();
        // SampleDetail leaves EvidenceQuality at its default (null)
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleDetail(runId)));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-evidence-quality", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-192: oc-eq-missing-areas rendered when MissingAreas non-empty ─

    [Fact]
    public async Task BlazorRunDetail_EvidenceQuality_MissingAreasRendered()
    {
        var runId  = Guid.NewGuid();
        var eq     = SampleEvidenceQuality(EvidenceStrength.Weak,
                         ["Triage Briefing", "Auth Signals"]);
        var detail = SampleDetail(runId) with { EvidenceQuality = eq };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-eq-missing-areas", body, StringComparison.Ordinal);
            Assert.Contains("Triage Briefing",      body, StringComparison.Ordinal);
            Assert.Contains("Auth Signals",         body, StringComparison.Ordinal);
        }
    }

    // ───── AC-193: oc-eq-missing-areas absent when MissingAreas empty ───────

    [Fact]
    public async Task BlazorRunDetail_EvidenceQuality_MissingAreasAbsentWhenEmpty()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { EvidenceQuality = SampleEvidenceQuality() };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-eq-missing-areas", body, StringComparison.Ordinal);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Slice 99 — Operator Decision Pack (AC-194 – AC-197)
    // ─────────────────────────────────────────────────────────────────────────

    private static OperatorDecisionPack SampleDecisionPack() =>
        new(IncidentSeverity:   "ok",
            IncidentAssessment: "Looks stable.",
            RecommendedActions: ["Check logs"],
            KeyFindings:        ["Tool success 75%"],
            EvidenceStrength:   EvidenceStrength.Moderate,
            EvidenceGuidance:   "Review runbook.",
            GeneratedAt:        DateTimeOffset.UtcNow);

    // ───── AC-194: oc-decision-pack renders when DecisionPack present ─────

    [Fact]
    public async Task BlazorRunDetail_DecisionPack_RendersOcDecisionPackSection()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { DecisionPack = SampleDecisionPack() };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.Contains("oc-decision-pack", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-195: oc-decision-pack absent when DecisionPack null ────────

    [Fact]
    public async Task BlazorRunDetail_DecisionPack_AbsentWhenNull()
    {
        var runId  = Guid.NewGuid();
        // SampleDetail leaves DecisionPack at its default (null)
        var detail = SampleDetail(runId);
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("oc-decision-pack", body, StringComparison.Ordinal);
        }
    }

    // ───── AC-196: no raw payload columns in rendered HTML ───────────────

    [Fact]
    public async Task BlazorRunDetail_DecisionPack_NoRawPayloadInHtml()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { DecisionPack = SampleDecisionPack() };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("CitationsJson", body, StringComparison.Ordinal);
            Assert.DoesNotContain("SummaryJson",   body, StringComparison.Ordinal);
        }
    }

    // ───── AC-197: decision pack section contains no mutation elements ────

    [Fact]
    public async Task BlazorRunDetail_DecisionPack_NoMutationElement()
    {
        var runId  = Guid.NewGuid();
        var detail = SampleDetail(runId) with { DecisionPack = SampleDecisionPack() };
        var (app, client, _) = await CreateBlazorTestHost(mock =>
            mock.Setup(s => s.GetRunDetailAsync(runId, "t1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(detail));

        await using (app)
        {
            var body = await (await client.GetAsync($"/app/runs/{runId}?tenantId=t1"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain("<form",        body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("method=\"post\"", body, StringComparison.OrdinalIgnoreCase);
        }
    }
}
