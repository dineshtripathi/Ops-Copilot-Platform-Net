using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Application.Orchestration;
using OpsCopilot.AgentRuns.Application.Acl;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.AgentRuns.Presentation.Contracts;
using OpsCopilot.AgentRuns.Presentation.Endpoints;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

public sealed class RunbookCitationIntegrationTests
{
    private const string TenantId    = "tenant-citation-test";
    private const string WorkspaceId = "00000000-0000-0000-0000-000000000002";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // -------------------------------------------------------------------------
    // Happy path: both tools succeed, single runbook hit is mapped to response
    // -------------------------------------------------------------------------
    [Fact]
    public async Task BothToolsSucceed_ResponseContainsRunbookCitations()
    {
        var runbook  = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);
        var allowlist = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        var budget   = new Mock<ITokenBudgetPolicy>(MockBehavior.Strict);
        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);

        allowlist.Setup(a => a.CanUseTool(It.IsAny<string>(), It.IsAny<string>()))
                 .Returns(PolicyDecision.Allow());
        budget.Setup(b => b.CheckRunBudget(It.IsAny<string>(), It.IsAny<Guid>()))
              .Returns(BudgetDecision.Allow());

        runbook.Setup(r => r.ExecuteAsync(It.IsAny<RunbookSearchToolRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new RunbookSearchToolResponse(
                   true,
                   new List<RunbookSearchHit>
                   {
                       new("high-cpu", "High CPU Runbook", "Check top processes and load average.", 0.85)
                   },
                   "cpu spike", null));

        var (app, client) = await CreateTestHostAsync(runbook, allowlist, budget, degraded);
        try
        {
            var response = await PostTriage(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TriageResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.Equal("Completed", body!.Status);
            Assert.NotEmpty(body.RunbookCitations);
            Assert.Equal("high-cpu", body.RunbookCitations[0].RunbookId);
            Assert.Equal("High CPU Runbook", body.RunbookCitations[0].Title);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // -------------------------------------------------------------------------
    // Allowlist denies runbook_search: citations empty, run still completes
    // -------------------------------------------------------------------------
    [Fact]
    public async Task RunbookAllowlistDenied_EmptyRunbookCitations_RunCompletes()
    {
        var runbook  = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);
        var allowlist = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        var budget   = new Mock<ITokenBudgetPolicy>(MockBehavior.Strict);
        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);

        allowlist.Setup(a => a.CanUseTool(It.IsAny<string>(), "kql_query"))
                 .Returns(PolicyDecision.Allow());
        allowlist.Setup(a => a.CanUseTool(It.IsAny<string>(), "runbook_search"))
                 .Returns(PolicyDecision.Deny("TOOL_NOT_ALLOWED", "Runbook search not permitted for this tenant."));

        budget.Setup(b => b.CheckRunBudget(It.IsAny<string>(), It.IsAny<Guid>()))
              .Returns(BudgetDecision.Allow());

        // runbook.ExecuteAsync must NOT be called (Strict validates this)

        var (app, client) = await CreateTestHostAsync(runbook, allowlist, budget, degraded);
        try
        {
            var response = await PostTriage(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TriageResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.Equal("Completed", body!.Status);
            Assert.Empty(body.RunbookCitations);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // -------------------------------------------------------------------------
    // Token budget exhausted before runbook_search: citations empty, run completes
    // -------------------------------------------------------------------------
    [Fact]
    public async Task RunbookBudgetDenied_EmptyRunbookCitations_RunCompletes()
    {
        var runbook  = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);
        var allowlist = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        var budget   = new Mock<ITokenBudgetPolicy>(MockBehavior.Strict);
        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);

        allowlist.Setup(a => a.CanUseTool(It.IsAny<string>(), It.IsAny<string>()))
                 .Returns(PolicyDecision.Allow());

        budget.SetupSequence(b => b.CheckRunBudget(It.IsAny<string>(), It.IsAny<Guid>()))
              .Returns(BudgetDecision.Allow())
              .Returns(BudgetDecision.Deny("BUDGET_EXHAUSTED", "Token budget exhausted for this run."));

        // runbook.ExecuteAsync must NOT be called (Strict validates this)

        var (app, client) = await CreateTestHostAsync(runbook, allowlist, budget, degraded);
        try
        {
            var response = await PostTriage(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TriageResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.Equal("Completed", body!.Status);
            Assert.Empty(body.RunbookCitations);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // -------------------------------------------------------------------------
    // runbook tool throws: degraded path taken, citations empty, run still completes
    // -------------------------------------------------------------------------
    [Fact]
    public async Task RunbookToolThrows_EmptyRunbookCitations_RunCompletes()
    {
        var runbook  = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);
        var allowlist = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        var budget   = new Mock<ITokenBudgetPolicy>(MockBehavior.Strict);
        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);

        allowlist.Setup(a => a.CanUseTool(It.IsAny<string>(), It.IsAny<string>()))
                 .Returns(PolicyDecision.Allow());
        budget.Setup(b => b.CheckRunBudget(It.IsAny<string>(), It.IsAny<Guid>()))
              .Returns(BudgetDecision.Allow());

        runbook.Setup(r => r.ExecuteAsync(It.IsAny<RunbookSearchToolRequest>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new Exception("mcp-error: backend unavailable"));

        degraded.Setup(d => d.MapFailure(It.IsAny<Exception>()))
                .Returns(new DegradedDecision(true, "UNKNOWN_FAILURE", "An unexpected error occurred.", false));

        var (app, client) = await CreateTestHostAsync(runbook, allowlist, budget, degraded);
        try
        {
            var response = await PostTriage(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TriageResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.Equal("Completed", body!.Status);
            Assert.Empty(body.RunbookCitations);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // -------------------------------------------------------------------------
    // runbook returns Ok:false (no exception): citations empty, run completes
    // -------------------------------------------------------------------------
    [Fact]
    public async Task RunbookReturnsNotOk_EmptyRunbookCitations_RunCompletes()
    {
        var runbook  = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);
        var allowlist = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        var budget   = new Mock<ITokenBudgetPolicy>(MockBehavior.Strict);
        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);

        allowlist.Setup(a => a.CanUseTool(It.IsAny<string>(), It.IsAny<string>()))
                 .Returns(PolicyDecision.Allow());
        budget.Setup(b => b.CheckRunBudget(It.IsAny<string>(), It.IsAny<Guid>()))
              .Returns(BudgetDecision.Allow());

        runbook.Setup(r => r.ExecuteAsync(It.IsAny<RunbookSearchToolRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new RunbookSearchToolResponse(false, [], "cpu spike", "service unavailable"));

        // degraded.MapFailure must NOT be called — Ok:false is not an exception path

        var (app, client) = await CreateTestHostAsync(runbook, allowlist, budget, degraded);
        try
        {
            var response = await PostTriage(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TriageResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.Equal("Completed", body!.Status);
            Assert.Empty(body.RunbookCitations);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // -------------------------------------------------------------------------
    // runbook returns multiple hits: all hits are mapped to RunbookCitations
    // -------------------------------------------------------------------------
    [Fact]
    public async Task RunbookReturnsMultipleHits_AllMappedToResponse()
    {
        var runbook  = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);
        var allowlist = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        var budget   = new Mock<ITokenBudgetPolicy>(MockBehavior.Strict);
        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);

        allowlist.Setup(a => a.CanUseTool(It.IsAny<string>(), It.IsAny<string>()))
                 .Returns(PolicyDecision.Allow());
        budget.Setup(b => b.CheckRunBudget(It.IsAny<string>(), It.IsAny<Guid>()))
              .Returns(BudgetDecision.Allow());

        runbook.Setup(r => r.ExecuteAsync(It.IsAny<RunbookSearchToolRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new RunbookSearchToolResponse(
                   true,
                   new List<RunbookSearchHit>
                   {
                       new("rb-cpu",  "CPU Spike Runbook",    "Scale up compute nodes.",         0.91),
                       new("rb-mem",  "Memory Leak Runbook",  "Identify leaking service.",        0.87),
                       new("rb-disk", "Disk Full Runbook",    "Prune old logs and temp files.",   0.82)
                   },
                   "resource saturation", null));

        var (app, client) = await CreateTestHostAsync(runbook, allowlist, budget, degraded);
        try
        {
            var response = await PostTriage(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TriageResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.Equal("Completed", body!.Status);
            Assert.Equal(3, body!.RunbookCitations.Count);
            Assert.Equal("rb-cpu",  body.RunbookCitations[0].RunbookId);
            Assert.Equal("rb-mem",  body.RunbookCitations[1].RunbookId);
            Assert.Equal("rb-disk", body.RunbookCitations[2].RunbookId);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static async Task<(WebApplication App, HttpClient Client)> CreateTestHostAsync(
        Mock<IRunbookSearchToolClient> runbook,
        Mock<IToolAllowlistPolicy>     allowlist,
        Mock<ITokenBudgetPolicy>       budget,
        Mock<IDegradedModePolicy>      degraded)
    {
        var agentRun = AgentRun.Create(TenantId, "test-fingerprint");

        var repo = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        repo.Setup(r => r.CreateRunAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentRun);
        repo.Setup(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.CompleteRunAsync(
                agentRun.RunId, AgentRunStatus.Completed,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kql = new Mock<IKqlToolClient>(MockBehavior.Strict);
        kql.Setup(k => k.ExecuteAsync(It.IsAny<KqlToolRequest>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new KqlToolResponse(
               true,
               new List<IReadOnlyDictionary<string, object?>>
               {
                   new Dictionary<string, object?> { ["cpu_pct"] = 95 }
               },
               "KustoQuery",
               WorkspaceId,
               "PT30M",
               DateTimeOffset.UtcNow,
               null));

        var sessionPolicy = new Mock<ISessionPolicy>(MockBehavior.Strict);
        sessionPolicy.Setup(p => p.GetSessionTtl(It.IsAny<string>()))
                     .Returns(TimeSpan.FromMinutes(30));

        var sessionStore = new Mock<ISessionStore>(MockBehavior.Strict);
        sessionStore.Setup(s => s.CreateAsync(
                It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tenantId, TimeSpan ttl, CancellationToken _) =>
                new SessionInfo(Guid.NewGuid(), tenantId, DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.Add(ttl), true));

        var orchestrator = new TriageOrchestrator(
            repo.Object, kql.Object, runbook.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System, new PermissiveRunbookAclFilter());

        var enricher = new Mock<IPackTriageEnricher>(MockBehavior.Strict);
        enricher.Setup(e => e.EnrichAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PackTriageEnrichment([], [], []));

        var executor = new Mock<IPackEvidenceExecutor>(MockBehavior.Strict);
        executor.Setup(e => e.ExecuteAsync(
                It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackEvidenceExecutionResult([], []));

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WORKSPACE_ID"] = WorkspaceId
        });
        builder.Services.AddSingleton(orchestrator);
        builder.Services.AddSingleton<IPackTriageEnricher>(enricher.Object);
        builder.Services.AddSingleton<IPackEvidenceExecutor>(executor.Object);

        var proposer = new Mock<IPackSafeActionProposer>();
        proposer.Setup(p => p.ProposeAsync(
                It.IsAny<PackSafeActionProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackSafeActionProposalResult([], []));
        builder.Services.AddSingleton<IPackSafeActionProposer>(proposer.Object);

        var recorder = new Mock<IPackSafeActionRecorder>();
        recorder.Setup(r => r.RecordAsync(
                It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackSafeActionRecordResult([], 0, 0, 0, []));
        builder.Services.AddSingleton<IPackSafeActionRecorder>(recorder.Object);

        var app = builder.Build();
        app.MapAgentRunEndpoints();
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    private static async Task PostTriage_Internal(
        HttpClient client, string workspaceId, string tenantId,
        Func<HttpResponseMessage, Task> assert)
    {
        var response = await PostTriageWith(client, tenantId, workspaceId);
        await assert(response);
    }

    private static async Task<HttpResponseMessage> PostTriage(HttpClient client)
        => await PostTriageWith(client, TenantId, WorkspaceId);

    private static async Task<HttpResponseMessage> PostTriageWith(
        HttpClient client, string tenantId, string workspaceId)
    {
        var request = new TriageRequest(
            AlertPayload: new AlertPayloadDto(
                AlertSource: "AzureMonitor",
                Fingerprint: "integration-test-fingerprint"),
            TimeRangeMinutes: 30,
            WorkspaceId: workspaceId);

        var msg = new HttpRequestMessage(HttpMethod.Post, "/agent/triage")
        {
            Content = JsonContent.Create(request)
        };
        msg.Headers.Add("x-tenant-id", tenantId);
        return await client.SendAsync(msg);
    }

    private static async Task DisposeHost(WebApplication app)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
