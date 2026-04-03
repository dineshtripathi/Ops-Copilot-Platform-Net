using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Application.Orchestration;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Models;
using OpsCopilot.AgentRuns.Presentation.Endpoints;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

public sealed class AgUiStreamTests
{
    // ── Valid request body template ──────────────────────────────────────────

    private static string ValidBody(string? workspaceId = "00000000-0000-0000-0000-000000000001") =>
        JsonSerializer.Serialize(new
        {
            alertPayload = new
            {
                alertSource = "azuremonitor",
                fingerprint = "fp-test-123",
                signalType  = "metric",
                resourceId  = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/providers/X/Y",
                serviceName = "svc",
                title       = "Test Alert"
            },
            workspaceId      = workspaceId,
            timeRangeMinutes = 60
        });

    // ── TriageResult helper ──────────────────────────────────────────────────

    private static TriageResult MakeResult(string? narrative = null) => new(
        RunId:                    Guid.NewGuid(),
        Status:                   AgentRunStatus.Completed,
        SummaryJson:              null,
        Citations:                Array.Empty<KqlCitation>(),
        RunbookCitations:         Array.Empty<RunbookCitation>(),
        MemoryCitations:          Array.Empty<MemoryCitation>(),
        DeploymentDiffCitations:  Array.Empty<DeploymentDiffCitation>(),
        SessionId:                null,
        IsNewSession:             false,
        SessionExpiresAtUtc:      null,
        UsedSessionContext:       false,
        SessionReasonCode:        "NoSession",
        LlmNarrative:             narrative);

    // ── Mock pack results (empty — no proposals/items) ───────────────────────

    private static (
        Mock<ITriageOrchestrator>     Orch,
        Mock<IPackTriageEnricher>     Enr,
        Mock<IPackEvidenceExecutor>   Ev,
        Mock<IPackSafeActionProposer> Prop,
        Mock<IPackSafeActionRecorder> Rec
    ) BuildMocks(TriageResult result)
    {
        var orch = new Mock<ITriageOrchestrator>(MockBehavior.Strict);
        orch.Setup(o => o.RunAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<RunContext?>(),
                It.IsAny<AgentRun?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var enr = new Mock<IPackTriageEnricher>(MockBehavior.Strict);
        enr.Setup(e => e.EnrichAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTriageEnrichment(
                Array.Empty<PackRunbookDetail>(),
                Array.Empty<PackEvidenceCollectorDetail>(),
                Array.Empty<string>()));

        var ev = new Mock<IPackEvidenceExecutor>(MockBehavior.Strict);
        ev.Setup(e => e.ExecuteAsync(
                It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackEvidenceExecutionResult(
                Array.Empty<PackEvidenceItem>(),
                Array.Empty<string>()));

        var prop = new Mock<IPackSafeActionProposer>(MockBehavior.Strict);
        prop.Setup(p => p.ProposeAsync(
                It.IsAny<PackSafeActionProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackSafeActionProposalResult(
                Array.Empty<PackSafeActionProposalItem>(),
                Array.Empty<string>()));

        var rec = new Mock<IPackSafeActionRecorder>(MockBehavior.Strict);
        rec.Setup(r => r.RecordAsync(
                It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackSafeActionRecordResult(
                Array.Empty<PackSafeActionRecordItem>(),
                CreatedCount: 0,
                SkippedCount: 0,
                FailedCount:  0,
                Array.Empty<string>()));

        return (orch, enr, ev, prop, rec);
    }

    // ── Test host factory ────────────────────────────────────────────────────

    private static async Task<(WebApplication App, HttpClient Client)> CreateTestHostAsync(
        Mock<ITriageOrchestrator>     orch,
        Mock<IPackTriageEnricher>     enr,
        Mock<IPackEvidenceExecutor>   ev,
        Mock<IPackSafeActionProposer> prop,
        Mock<IPackSafeActionRecorder> rec)
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<ITriageOrchestrator>(orch.Object);
        builder.Services.AddSingleton<IPackTriageEnricher>(enr.Object);
        builder.Services.AddSingleton<IPackEvidenceExecutor>(ev.Object);
        builder.Services.AddSingleton<IPackSafeActionProposer>(prop.Object);
        builder.Services.AddSingleton<IPackSafeActionRecorder>(rec.Object);
        // ChatOrchestrator is a concrete type used directly in /agent/chat.
        // ASP.NET Core 10 lazily compiles ALL endpoint handlers on first request,
        // so it must be resolvable even when tests only call /agent/triage/stream.
        builder.Services.AddSingleton<IIncidentMemoryService>(Mock.Of<IIncidentMemoryService>());
        builder.Services.AddSingleton<IRunbookSearchToolClient>(Mock.Of<IRunbookSearchToolClient>());
        builder.Services.AddSingleton<IRunbookAclFilter>(Mock.Of<IRunbookAclFilter>());
        builder.Services.AddScoped<ChatOrchestrator>();
        var app = builder.Build();
        app.MapAgentRunEndpoints();
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    // ── SSE parse helper ─────────────────────────────────────────────────────

    private static List<JsonDocument> ParseSseEvents(string body)
    {
        return body
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.StartsWith("data: ", StringComparison.Ordinal))
            .Select(l => JsonDocument.Parse(l[6..]))
            .ToList();
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MissingTenantHeader_Returns400()
    {
        var looseOrch = new Mock<ITriageOrchestrator>();
        var looseEnr  = new Mock<IPackTriageEnricher>();
        var looseEv   = new Mock<IPackEvidenceExecutor>();
        var looseProp = new Mock<IPackSafeActionProposer>();
        var looseRec  = new Mock<IPackSafeActionRecorder>();

        var (app, client) = await CreateTestHostAsync(looseOrch, looseEnr, looseEv, looseProp, looseRec);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/agent/triage/stream");
            request.Content = new StringContent(ValidBody(), System.Text.Encoding.UTF8, "application/json");
            // No x-tenant-id header

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task MissingAlertPayload_Returns400()
    {
        var looseOrch = new Mock<ITriageOrchestrator>();
        var looseEnr  = new Mock<IPackTriageEnricher>();
        var looseEv   = new Mock<IPackEvidenceExecutor>();
        var looseProp = new Mock<IPackSafeActionProposer>();
        var looseRec  = new Mock<IPackSafeActionRecorder>();

        var (app, client) = await CreateTestHostAsync(looseOrch, looseEnr, looseEv, looseProp, looseRec);
        try
        {
            var body = JsonSerializer.Serialize(new
            {
                workspaceId      = "00000000-0000-0000-0000-000000000001",
                timeRangeMinutes = 60
                // alertPayload omitted → null
            });

            var request = new HttpRequestMessage(HttpMethod.Post, "/agent/triage/stream");
            request.Headers.Add("x-tenant-id", "tenant-001");
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task ValidRequest_FirstEventIsRunStarted()
    {
        var result = MakeResult();
        var (orch, enr, ev, prop, rec) = BuildMocks(result);
        var (app, client) = await CreateTestHostAsync(orch, enr, ev, prop, rec);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/agent/triage/stream");
            request.Headers.Add("x-tenant-id", "tenant-001");
            request.Content = new StringContent(ValidBody(), System.Text.Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            var events = ParseSseEvents(body);

            Assert.NotEmpty(events);
            var type = events[0].RootElement.GetProperty("type").GetString();
            Assert.Equal("RunStarted", type);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task ValidRequest_WithNarrative_EmitsTextMessageStart()
    {
        var result = MakeResult("Service is degraded. Dependency timeout detected.");
        var (orch, enr, ev, prop, rec) = BuildMocks(result);
        var (app, client) = await CreateTestHostAsync(orch, enr, ev, prop, rec);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/agent/triage/stream");
            request.Headers.Add("x-tenant-id", "tenant-001");
            request.Content = new StringContent(ValidBody(), System.Text.Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            var events = ParseSseEvents(body);

            var startEvent = events.FirstOrDefault(e =>
                e.RootElement.TryGetProperty("type", out var t) &&
                t.GetString() == "TextMessageStart");

            Assert.NotNull(startEvent);
            Assert.Equal("assistant", startEvent!.RootElement.GetProperty("role").GetString());
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task ValidRequest_WithNarrative_EmitsTextMessageContent()
    {
        var result = MakeResult("Service is degraded. Dependency timeout detected.");
        var (orch, enr, ev, prop, rec) = BuildMocks(result);
        var (app, client) = await CreateTestHostAsync(orch, enr, ev, prop, rec);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/agent/triage/stream");
            request.Headers.Add("x-tenant-id", "tenant-001");
            request.Content = new StringContent(ValidBody(), System.Text.Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            var events = ParseSseEvents(body);

            var contentEvents = events.Where(e =>
                e.RootElement.TryGetProperty("type", out var t) &&
                t.GetString() == "TextMessageContent").ToList();

            Assert.NotEmpty(contentEvents);
            foreach (var e in contentEvents)
                Assert.False(string.IsNullOrEmpty(e.RootElement.GetProperty("delta").GetString()));
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task ValidRequest_WithNarrative_EmitsTextMessageEnd()
    {
        var result = MakeResult("Service is degraded. Dependency timeout detected.");
        var (orch, enr, ev, prop, rec) = BuildMocks(result);
        var (app, client) = await CreateTestHostAsync(orch, enr, ev, prop, rec);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/agent/triage/stream");
            request.Headers.Add("x-tenant-id", "tenant-001");
            request.Content = new StringContent(ValidBody(), System.Text.Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            var events = ParseSseEvents(body);

            var endEvent = events.FirstOrDefault(e =>
                e.RootElement.TryGetProperty("type", out var t) &&
                t.GetString() == "TextMessageEnd");

            Assert.NotNull(endEvent);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task ValidRequest_LastEventIsRunFinished()
    {
        var result = MakeResult("Analysis complete.");
        var (orch, enr, ev, prop, rec) = BuildMocks(result);
        var (app, client) = await CreateTestHostAsync(orch, enr, ev, prop, rec);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/agent/triage/stream");
            request.Headers.Add("x-tenant-id", "tenant-001");
            request.Content = new StringContent(ValidBody(), System.Text.Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            var events = ParseSseEvents(body);

            Assert.NotEmpty(events);
            var lastType = events[^1].RootElement.GetProperty("type").GetString();
            Assert.Equal("RunFinished", lastType);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task SessionTenantMismatch_EmitsRunError()
    {
        var orchMock = new Mock<ITriageOrchestrator>(MockBehavior.Strict);
        orchMock.Setup(o => o.RunAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<RunContext?>(),
                It.IsAny<AgentRun?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SessionTenantMismatchException(
                Guid.NewGuid(), "owner-tenant", "caller-tenant"));

        var looseEnr  = new Mock<IPackTriageEnricher>();
        var looseEv   = new Mock<IPackEvidenceExecutor>();
        var looseProp = new Mock<IPackSafeActionProposer>();
        var looseRec  = new Mock<IPackSafeActionRecorder>();

        var (app, client) = await CreateTestHostAsync(orchMock, looseEnr, looseEv, looseProp, looseRec);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/agent/triage/stream");
            request.Headers.Add("x-tenant-id", "tenant-001");
            request.Content = new StringContent(ValidBody(), System.Text.Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            var events = ParseSseEvents(body);

            var errorEvent = events.FirstOrDefault(e =>
                e.RootElement.TryGetProperty("type", out var t) &&
                t.GetString() == "RunError");

            Assert.NotNull(errorEvent);
            Assert.False(string.IsNullOrEmpty(errorEvent!.RootElement.GetProperty("message").GetString()));
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task ValidRequest_NoNarrative_NoTextMessageEvents()
    {
        var result = MakeResult(narrative: null);
        var (orch, enr, ev, prop, rec) = BuildMocks(result);
        var (app, client) = await CreateTestHostAsync(orch, enr, ev, prop, rec);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/agent/triage/stream");
            request.Headers.Add("x-tenant-id", "tenant-001");
            request.Content = new StringContent(ValidBody(), System.Text.Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            var events = ParseSseEvents(body);

            var textMessageEvents = events.Where(e =>
                e.RootElement.TryGetProperty("type", out var t) &&
                (t.GetString() ?? "").StartsWith("TextMessage", StringComparison.Ordinal)).ToList();

            Assert.Empty(textMessageEvents);
        }
        finally { await app.StopAsync(); }
    }
}
