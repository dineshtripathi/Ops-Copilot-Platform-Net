using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Application.Orchestration;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

/// <summary>
/// Integration-level tests confirming <see cref="ITargetScopeEvaluator"/> (Guardrail 2.5)
/// workspace scope enforcement inside <see cref="TriageOrchestrator"/> (Slice 57).
/// Uses the same direct-constructor/mock pattern as <see cref="TriageOrchestratorTests"/>
/// — no HTTP pipeline required.
/// </summary>
public sealed class KqlGovernedEvidenceIntegrationTests
{
    private const string TenantId        = "tenant-scope-integ";
    private const string AlertFingerprint = "fp-scope-integ";
    private const string WorkspaceId     = "ws-integ-001";
    private const int    Minutes          = 120;

    // ─────────────────────────────────────────────────────────────────────────
    // Allow path: scope evaluator permits workspace → KQL runs → Completed
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WorkspaceScopeAllow_ProceedsToKqlAndReturnsCompleted()
    {
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock    = CreateHappyPathRepo(agentRun);
        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var scopeEvaluator = new Mock<ITargetScopeEvaluator>(MockBehavior.Strict);
        scopeEvaluator
            .Setup(x => x.Evaluate(TenantId, "LogAnalyticsWorkspace", WorkspaceId))
            .Returns(TargetScopeDecision.Allow());

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System,
            scopeEvaluator: scopeEvaluator.Object);

        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        Assert.Equal(AgentRunStatus.Completed, result.Status);

        // KQL was reached (not blocked by scope guardrail)
        kqlMock.Verify(
            k => k.ExecuteAsync(It.IsAny<KqlToolRequest>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        // Scope-allow policy event appended exactly once
        repoMock.Verify(
            r => r.AppendPolicyEventAsync(
                It.Is<AgentRunPolicyEvent>(e =>
                    e.PolicyName == nameof(ITargetScopeEvaluator) && e.Allowed),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Deny path: scope evaluator blocks workspace → KQL never called → Failed
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WorkspaceScopeDeny_FailsBeforeKql()
    {
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = CreateHappyPathRepo(agentRun);
        repoMock
            .Setup(r => r.CompleteRunAsync(
                agentRun.RunId, AgentRunStatus.Failed,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Strict mocks with no setups — any unexpected call will throw
        var kqlMock     = new Mock<IKqlToolClient>(MockBehavior.Strict);
        var runbookMock = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);

        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var scopeEvaluator = new Mock<ITargetScopeEvaluator>(MockBehavior.Strict);
        scopeEvaluator
            .Setup(x => x.Evaluate(TenantId, "LogAnalyticsWorkspace", WorkspaceId))
            .Returns(TargetScopeDecision.Deny("WORKSPACE_NOT_ALLOWED",
                $"Workspace '{WorkspaceId}' is not in the tenant's approved workspace list."));

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System,
            scopeEvaluator: scopeEvaluator.Object);

        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        Assert.Equal(AgentRunStatus.Failed, result.Status);

        // Strict mocks: any call to ExecuteAsync would have thrown above
        kqlMock.VerifyNoOtherCalls();
        runbookMock.VerifyNoOtherCalls();

        // Scope-deny policy event appended exactly once
        repoMock.Verify(
            r => r.AppendPolicyEventAsync(
                It.Is<AgentRunPolicyEvent>(e =>
                    e.PolicyName == nameof(ITargetScopeEvaluator) &&
                    !e.Allowed &&
                    e.ReasonCode == "WORKSPACE_NOT_ALLOWED"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static Mock<IAgentRunRepository> CreateHappyPathRepo(AgentRun agentRun)
    {
        var mock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        mock.Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentRun);
        mock.Setup(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(r => r.CompleteRunAsync(
                agentRun.RunId, AgentRunStatus.Completed,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static Mock<IKqlToolClient> CreateHappyPathKql()
    {
        var mock = new Mock<IKqlToolClient>(MockBehavior.Strict);
        mock.Setup(k => k.ExecuteAsync(It.IsAny<KqlToolRequest>(), It.IsAny<CancellationToken>()))
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
        return mock;
    }

    private static Mock<IRunbookSearchToolClient> CreateHappyPathRunbook()
    {
        var mock = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);
        mock.Setup(r => r.ExecuteAsync(It.IsAny<RunbookSearchToolRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunbookSearchToolResponse(
                Ok:    true,
                Hits:  new List<RunbookSearchHit>
                {
                    new("high-cpu", "High CPU Troubleshooting", "Check top processes...", 0.85)
                },
                Query: AlertFingerprint));
        return mock;
    }

    private static (Mock<IToolAllowlistPolicy> allowlist, Mock<ITokenBudgetPolicy> budget, Mock<IDegradedModePolicy> degraded)
        CreateAllowAllGovernanceMocks()
    {
        var allowlist = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        allowlist
            .Setup(a => a.CanUseTool(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(PolicyDecision.Allow());

        var budget = new Mock<ITokenBudgetPolicy>(MockBehavior.Strict);
        budget
            .Setup(b => b.CheckRunBudget(It.IsAny<string>(), It.IsAny<Guid>()))
            .Returns(BudgetDecision.Allow());

        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);
        degraded
            .Setup(d => d.MapFailure(It.IsAny<Exception>()))
            .Returns(new DegradedDecision(true, "UNKNOWN_FAILURE", "Unexpected error", false));

        return (allowlist, budget, degraded);
    }

    private static (Mock<ISessionStore> sessionStore, Mock<ISessionPolicy> sessionPolicy)
        CreateDefaultSessionMocks()
    {
        var sessionPolicy = new Mock<ISessionPolicy>(MockBehavior.Strict);
        sessionPolicy
            .Setup(p => p.GetSessionTtl(It.IsAny<string>()))
            .Returns(TimeSpan.FromMinutes(30));

        var sessionStore = new Mock<ISessionStore>(MockBehavior.Strict);
        sessionStore
            .Setup(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tenantId, TimeSpan ttl, CancellationToken _) =>
                new SessionInfo(Guid.NewGuid(), tenantId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.Add(ttl), true));

        return (sessionStore, sessionPolicy);
    }
}
