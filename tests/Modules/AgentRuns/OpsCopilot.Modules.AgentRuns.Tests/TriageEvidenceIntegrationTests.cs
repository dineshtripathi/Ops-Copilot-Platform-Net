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
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.AgentRuns.Presentation.Contracts;
using OpsCopilot.AgentRuns.Presentation.Endpoints;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

/// <summary>
/// Integration tests for the POST /agent/triage endpoint focusing on
/// Pack Evidence execution behaviour (Slice 38, TODO 9).
///
/// Uses <see cref="Microsoft.AspNetCore.TestHost"/> to spin up a real
/// ASP.NET pipeline.  <see cref="TriageOrchestrator"/> is instantiated
/// with mocked dependencies; <see cref="IPackEvidenceExecutor"/> is
/// mocked to control evidence behaviour per test.
/// </summary>
public sealed class TriageEvidenceIntegrationTests
{
    private const string TenantId    = "tenant-integration";
    private const string WorkspaceId = "00000000-0000-0000-0000-000000000001";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Mode B + enabled → response contains PackEvidenceResults
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ModeB_Enabled_ReturnsPackEvidenceResults()
    {
        var evidenceItems = new List<PackEvidenceItem>
        {
            new("cpu-pack", "cpu-col-1", "azure-monitor", "cpu.kql",
                "Perf | where CounterName == '% Processor Time'",
                """[{"Computer":"web-01","CounterValue":92.5}]""",
                1, null)
        };
        var evidenceResult = new PackEvidenceExecutionResult(evidenceItems, []);
        var (app, client) = await CreateTestHost("B", "true", evidenceResult);
        try
        {
            var response = await PostTriage(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TriageResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.NotNull(body.PackEvidenceResults);
            Assert.Single(body.PackEvidenceResults);

            var item = body.PackEvidenceResults[0];
            Assert.Equal("cpu-pack", item.PackName);
            Assert.Equal("cpu-col-1", item.CollectorId);
            Assert.Equal("azure-monitor", item.ConnectorName);
            Assert.Equal(1, item.RowCount);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Mode A → PackEvidenceResults is null
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ModeA_ReturnsNullPackEvidenceResults()
    {
        // Mode A → executor returns empty list → endpoint maps to null
        var evidenceResult = new PackEvidenceExecutionResult([], []);
        var (app, client) = await CreateTestHost("A", "true", evidenceResult);
        try
        {
            var response = await PostTriage(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TriageResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.Null(body.PackEvidenceResults);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Feature disabled → PackEvidenceResults is null
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FeatureDisabled_ReturnsNullPackEvidenceResults()
    {
        // Even Mode B, but executor returns empty (disabled at executor level)
        var evidenceResult = new PackEvidenceExecutionResult([], []);
        var (app, client) = await CreateTestHost("B", "false", evidenceResult);
        try
        {
            var response = await PostTriage(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TriageResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.Null(body.PackEvidenceResults);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Evidence errors don't fail triage → still 200 OK, Completed
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvidenceErrors_DoNotFailTriage_Returns200()
    {
        var evidenceItems = new List<PackEvidenceItem>
        {
            new("disk-pack", "disk-col-1", "azure-monitor", "disk.kql",
                "Perf | where CounterName == '% Free Space'",
                null, 0, "KQL execution timed out")
        };
        var evidenceResult = new PackEvidenceExecutionResult(
            evidenceItems,
            new List<string> { "Collector disk-col-1 failed: KQL execution timed out" });
        var (app, client) = await CreateTestHost("B", "true", evidenceResult);
        try
        {
            var response = await PostTriage(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TriageResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.Equal("Completed", body.Status);
            Assert.NotNull(body.PackEvidenceResults);
            Assert.Single(body.PackEvidenceResults);
            Assert.Equal("KQL execution timed out", body.PackEvidenceResults[0].ErrorMessage);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. Multiple evidence items aggregate correctly
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MultipleEvidenceItems_AggregateInResponse()
    {
        var evidenceItems = new List<PackEvidenceItem>
        {
            new("cpu-pack", "cpu-col-1", "azure-monitor", "cpu.kql",
                "Perf | where CounterName == '% Processor Time'",
                """[{"Computer":"web-01","CounterValue":92.5}]""",
                1, null),
            new("memory-pack", "mem-col-1", "azure-monitor", "mem.kql",
                "Perf | where CounterName == '% Used Memory'",
                """[{"Computer":"web-01","CounterValue":85.0}]""",
                1, null),
            new("disk-pack", "disk-col-1", "azure-monitor", "disk.kql",
                "Perf | where CounterName == '% Free Space'",
                """[{"Computer":"web-01","CounterValue":5.2}]""",
                1, null)
        };
        var evidenceResult = new PackEvidenceExecutionResult(evidenceItems, []);
        var (app, client) = await CreateTestHost("B", "true", evidenceResult);
        try
        {
            var response = await PostTriage(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TriageResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.NotNull(body.PackEvidenceResults);
            Assert.Equal(3, body.PackEvidenceResults.Count);
            Assert.Equal("cpu-pack", body.PackEvidenceResults[0].PackName);
            Assert.Equal("memory-pack", body.PackEvidenceResults[1].PackName);
            Assert.Equal("disk-pack", body.PackEvidenceResults[2].PackName);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. Evidence DTO fields map correctly through pipeline
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvidenceFields_MapCorrectlyThroughPipeline()
    {
        var evidenceItems = new List<PackEvidenceItem>
        {
            new("net-pack", "net-col-1", "azure-monitor", "network.kql",
                "AzureNetworkAnalytics | take 10",
                """[{"SourceIP":"10.0.0.1","DestIP":"10.0.0.2","Bytes":1024}]""",
                1, null)
        };
        var evidenceResult = new PackEvidenceExecutionResult(evidenceItems, []);
        var (app, client) = await CreateTestHost("B", "true", evidenceResult);
        try
        {
            var response = await PostTriage(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TriageResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.NotNull(body.PackEvidenceResults);
            var item = body.PackEvidenceResults[0];

            Assert.Equal("net-pack", item.PackName);
            Assert.Equal("net-col-1", item.CollectorId);
            Assert.Equal("azure-monitor", item.ConnectorName);
            Assert.Equal("network.kql", item.QueryFile);
            Assert.Equal("AzureNetworkAnalytics | take 10", item.QueryContent);
            Assert.Contains("SourceIP", item.ResultJson);
            Assert.Equal(1, item.RowCount);
            Assert.Null(item.ErrorMessage);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. Mode C includes evidence (higher than B)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ModeC_IncludesEvidenceResults()
    {
        var evidenceItems = new List<PackEvidenceItem>
        {
            new("security-pack", "sec-col-1", "azure-monitor", "security.kql",
                "SecurityEvent | take 5",
                """[{"Account":"admin","Activity":"4625 - An account failed to logon."}]""",
                1, null)
        };
        var evidenceResult = new PackEvidenceExecutionResult(evidenceItems, []);
        var (app, client) = await CreateTestHost("C", "true", evidenceResult);
        try
        {
            var response = await PostTriage(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TriageResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.NotNull(body.PackEvidenceResults);
            Assert.Single(body.PackEvidenceResults);
            Assert.Equal("security-pack", body.PackEvidenceResults[0].PackName);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. Empty evidence list → PackEvidenceResults is null
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmptyEvidenceList_ReturnsNullPackEvidenceResults()
    {
        var evidenceResult = new PackEvidenceExecutionResult([], []);
        var (app, client) = await CreateTestHost("B", "true", evidenceResult);
        try
        {
            var response = await PostTriage(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TriageResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.Null(body.PackEvidenceResults);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9. Evidence with mixed success and failure items
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MixedSuccessAndFailure_BothPresentInResults()
    {
        var evidenceItems = new List<PackEvidenceItem>
        {
            new("cpu-pack", "cpu-col-1", "azure-monitor", "cpu.kql",
                "Perf | where CounterName == '% Processor Time'",
                """[{"Computer":"web-01","CounterValue":92.5}]""",
                1, null),
            new("disk-pack", "disk-col-1", "azure-monitor", "disk.kql",
                "Perf | where CounterName == '% Free Space'",
                null, 0, "Query execution failed: timeout")
        };
        var evidenceResult = new PackEvidenceExecutionResult(
            evidenceItems,
            new List<string> { "Collector disk-col-1 failed" });
        var (app, client) = await CreateTestHost("B", "true", evidenceResult);
        try
        {
            var response = await PostTriage(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TriageResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.NotNull(body.PackEvidenceResults);
            Assert.Equal(2, body.PackEvidenceResults.Count);

            // Successful item
            var success = body.PackEvidenceResults[0];
            Assert.Equal("cpu-pack", success.PackName);
            Assert.Null(success.ErrorMessage);
            Assert.Equal(1, success.RowCount);

            // Failed item
            var failure = body.PackEvidenceResults[1];
            Assert.Equal("disk-pack", failure.PackName);
            Assert.NotNull(failure.ErrorMessage);
            Assert.Equal(0, failure.RowCount);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 10. RunId and status flow correctly alongside evidence
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunIdAndStatus_FlowCorrectlyAlongsideEvidence()
    {
        var evidenceItems = new List<PackEvidenceItem>
        {
            new("app-pack", "app-col-1", "azure-monitor", "app.kql",
                "AppRequests | take 5",
                """[{"Name":"/api/health","ResultCode":200}]""",
                1, null)
        };
        var evidenceResult = new PackEvidenceExecutionResult(evidenceItems, []);
        var (app, client) = await CreateTestHost("B", "true", evidenceResult);
        try
        {
            var response = await PostTriage(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TriageResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.NotEqual(Guid.Empty, body.RunId);
            Assert.Equal("Completed", body.Status);
            Assert.NotNull(body.PackEvidenceResults);
            Assert.Single(body.PackEvidenceResults);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Test Infrastructure — TestHost setup
    // ═════════════════════════════════════════════════════════════════════════

    private static async Task<(WebApplication App, HttpClient Client)> CreateTestHost(
        string deploymentMode,
        string evidenceEnabled,
        PackEvidenceExecutionResult evidenceResult)
    {
        // ── Configuration ───────────────────────────────────────────────
        var configDict = new Dictionary<string, string?>
        {
            ["WORKSPACE_ID"]                  = WorkspaceId,
            ["Packs:DeploymentMode"]          = deploymentMode,
            ["Packs:EvidenceExecutionEnabled"] = evidenceEnabled
        };

        // ── TriageOrchestrator dependencies (10 mocks) ──────────────────
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
                Ok:            true,
                Rows:          new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?> { ["message"] = "test log line" }
                },
                ExecutedQuery: "search * | where TimeGenerated > ago(30m) | take 20",
                WorkspaceId:   WorkspaceId,
                Timespan:      "PT30M",
                ExecutedAtUtc: DateTimeOffset.UtcNow,
                Error:         null));

        var runbook = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);
        runbook.Setup(r => r.ExecuteAsync(It.IsAny<RunbookSearchToolRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunbookSearchToolResponse(
                Ok:    true,
                Hits:  new List<RunbookSearchHit>
                {
                    new("high-cpu", "High CPU Troubleshooting", "Check top processes...", 0.85)
                },
                Query: "test-fingerprint"));

        var allowlist = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        allowlist.Setup(a => a.CanUseTool(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(PolicyDecision.Allow());

        var budget = new Mock<ITokenBudgetPolicy>(MockBehavior.Strict);
        budget.Setup(b => b.CheckRunBudget(It.IsAny<string>(), It.IsAny<Guid>()))
            .Returns(BudgetDecision.Allow());

        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);
        degraded.Setup(d => d.MapFailure(It.IsAny<Exception>()))
            .Returns(new DegradedDecision(true, "UNKNOWN_FAILURE", "Unexpected error", false));

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
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System);

        // ── Pack enricher (returns empty enrichment) ─────────────────────
        var enricher = new Mock<IPackTriageEnricher>(MockBehavior.Strict);
        enricher.Setup(e => e.EnrichAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackTriageEnrichment([], [], []));

        // ── Pack evidence executor (returns per-test result) ─────────────
        var executor = new Mock<IPackEvidenceExecutor>(MockBehavior.Strict);
        executor.Setup(e => e.ExecuteAsync(
                It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(evidenceResult);

        // ── Build host ──────────────────────────────────────────────────
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Configuration.AddInMemoryCollection(configDict);
        builder.Services.AddSingleton(orchestrator);
        builder.Services.AddSingleton<IPackTriageEnricher>(enricher.Object);
        builder.Services.AddSingleton<IPackEvidenceExecutor>(executor.Object);

        var proposer = new Mock<IPackSafeActionProposer>();
        proposer.Setup(p => p.ProposeAsync(It.IsAny<PackSafeActionProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackSafeActionProposalResult([], []));
        builder.Services.AddSingleton<IPackSafeActionProposer>(proposer.Object);

        var recorder = new Mock<IPackSafeActionRecorder>();
        recorder.Setup(r => r.RecordAsync(It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackSafeActionRecordResult([], 0, 0, 0, []));
        builder.Services.AddSingleton<IPackSafeActionRecorder>(recorder.Object);

        var app = builder.Build();
        app.MapAgentRunEndpoints();
        await app.StartAsync();

        return (app, app.GetTestClient());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Slice 40 — S40.1: ModeB + tenant passed → results present
    //   Verifies the endpoint passes tenantId from x-tenant-id header to
    //   PackEvidenceExecutionRequest so the workspace resolver can use it.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Slice40_ModeB_TenantIdPassedToExecutor_ReturnsResults()
    {
        string? capturedTenantId = null;
        var evidenceItems = new List<PackEvidenceItem>
        {
            new("cpu-pack", "cpu-col-1", "azure-monitor", "cpu.kql",
                "Perf | take 1", "[{\"cpu\":80}]", 1, null)
        };
        var evidenceResult = new PackEvidenceExecutionResult(evidenceItems, []);

        // Build a custom host where we capture the request's TenantId
        var configDict = new Dictionary<string, string?>
        {
            ["WORKSPACE_ID"]                  = WorkspaceId,
            ["Packs:DeploymentMode"]          = "B",
            ["Packs:EvidenceExecutionEnabled"] = "true"
        };

        var agentRun = AgentRun.Create(TenantId, "test-fingerprint");
        var repo = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        repo.Setup(r => r.CreateRunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentRun);
        repo.Setup(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.CompleteRunAsync(agentRun.RunId, AgentRunStatus.Completed, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kql = new Mock<IKqlToolClient>(MockBehavior.Strict);
        kql.Setup(k => k.ExecuteAsync(It.IsAny<KqlToolRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KqlToolResponse(true, new List<IReadOnlyDictionary<string, object?>> { new Dictionary<string, object?> { ["x"] = 1 } }, "q", WorkspaceId, "PT30M", DateTimeOffset.UtcNow, null));

        var runbook = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);
        runbook.Setup(r => r.ExecuteAsync(It.IsAny<RunbookSearchToolRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunbookSearchToolResponse(true, new List<RunbookSearchHit> { new("rb1", "Runbook", "...", 0.9) }, "q"));

        var allowlist = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        allowlist.Setup(a => a.CanUseTool(It.IsAny<string>(), It.IsAny<string>())).Returns(PolicyDecision.Allow());
        var budget = new Mock<ITokenBudgetPolicy>(MockBehavior.Strict);
        budget.Setup(b => b.CheckRunBudget(It.IsAny<string>(), It.IsAny<Guid>())).Returns(BudgetDecision.Allow());
        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);
        degraded.Setup(d => d.MapFailure(It.IsAny<Exception>())).Returns(new DegradedDecision(true, "UNKNOWN_FAILURE", "Unexpected error", false));
        var sessionPolicy = new Mock<ISessionPolicy>(MockBehavior.Strict);
        sessionPolicy.Setup(p => p.GetSessionTtl(It.IsAny<string>())).Returns(TimeSpan.FromMinutes(30));
        var sessionStore = new Mock<ISessionStore>(MockBehavior.Strict);
        sessionStore.Setup(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tenantId, TimeSpan ttl, CancellationToken _) =>
                new SessionInfo(Guid.NewGuid(), tenantId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.Add(ttl), true));

        var orchestrator = new TriageOrchestrator(repo.Object, kql.Object, runbook.Object, NullLogger<TriageOrchestrator>.Instance, allowlist.Object, budget.Object, degraded.Object, sessionStore.Object, sessionPolicy.Object, TimeProvider.System);
        var enricher = new Mock<IPackTriageEnricher>(MockBehavior.Strict);
        enricher.Setup(e => e.EnrichAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new PackTriageEnrichment([], [], []));

        var executor = new Mock<IPackEvidenceExecutor>(MockBehavior.Strict);
        executor.Setup(e => e.ExecuteAsync(It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PackEvidenceExecutionRequest, CancellationToken>((req, _) => capturedTenantId = req.TenantId)
            .ReturnsAsync(evidenceResult);

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(configDict);
        builder.Services.AddSingleton(orchestrator);
        builder.Services.AddSingleton<IPackTriageEnricher>(enricher.Object);
        builder.Services.AddSingleton<IPackEvidenceExecutor>(executor.Object);

        var proposer = new Mock<IPackSafeActionProposer>();
        proposer.Setup(p => p.ProposeAsync(It.IsAny<PackSafeActionProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackSafeActionProposalResult([], []));
        builder.Services.AddSingleton<IPackSafeActionProposer>(proposer.Object);

        var recorder = new Mock<IPackSafeActionRecorder>();
        recorder.Setup(r => r.RecordAsync(It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackSafeActionRecordResult([], 0, 0, 0, []));
        builder.Services.AddSingleton<IPackSafeActionRecorder>(recorder.Object);

        var app = builder.Build();
        app.MapAgentRunEndpoints();
        await app.StartAsync();
        var client = app.GetTestClient();

        try
        {
            var response = await PostTriage(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TriageResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.NotNull(body.PackEvidenceResults);
            Assert.Single(body.PackEvidenceResults);

            // Verify tenantId from header was forwarded to the executor
            Assert.Equal(TenantId, capturedTenantId);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Slice 40 — S40.2: Missing workspace → per-item errors, 200 OK
    //   Simulates the workspace resolver returning missing_workspace for every
    //   eligible collector. Triage must still return 200; errors are per-item.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Slice40_MissingWorkspace_PerItemErrors_Returns200()
    {
        // Executor returns per-item errors as produced by the workspace-failure path
        var evidenceItems = new List<PackEvidenceItem>
        {
            new("cpu-pack", "cpu-col-1", "azure-monitor", "cpu.kql",
                null, null, 0, "Workspace not configured for tenant"),
            new("mem-pack", "mem-col-1", "azure-monitor", "mem.kql",
                null, null, 0, "Workspace not configured for tenant")
        };
        var evidenceResult = new PackEvidenceExecutionResult(
            evidenceItems,
            new List<string>
            {
                "Pack 'cpu-pack' collector 'cpu-col-1': missing_workspace",
                "Pack 'mem-pack' collector 'mem-col-1': missing_workspace"
            });

        var (app, client) = await CreateTestHost("B", "true", evidenceResult);
        try
        {
            var response = await PostTriage(client);

            // Triage must NOT fail — workspace errors are non-fatal
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TriageResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.Equal("Completed", body.Status);

            // Both error items present
            Assert.NotNull(body.PackEvidenceResults);
            Assert.Equal(2, body.PackEvidenceResults.Count);
            Assert.All(body.PackEvidenceResults, item =>
            {
                Assert.Equal("Workspace not configured for tenant", item.ErrorMessage);
                Assert.Equal(0, item.RowCount);
                Assert.Null(item.ResultJson);
            });
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    private static async Task DisposeHost(WebApplication app)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    private static async Task<HttpResponseMessage> PostTriage(HttpClient client)
    {
        var request = new TriageRequest(
            AlertPayload: new AlertPayloadDto(
                AlertSource: "AzureMonitor",
                Fingerprint: "integration-test-fingerprint"),
            TimeRangeMinutes: 30,
            WorkspaceId: WorkspaceId);

        var msg = new HttpRequestMessage(HttpMethod.Post, "/agent/triage")
        {
            Content = JsonContent.Create(request)
        };
        msg.Headers.Add("x-tenant-id", TenantId);
        return await client.SendAsync(msg);
    }
}
