using Microsoft.Extensions.Logging;
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
/// Unit tests for <see cref="TriageOrchestrator"/>.
/// 
/// Scope: orchestration logic only — no IO.
/// Dependencies are stubbed via Moq.
/// Slice 3A: all tests now include governance policy mocks.
/// </summary>
public sealed class TriageOrchestratorTests
{
    private const string TenantId         = "tenant-001";
    private const string AlertFingerprint = "AABBCCDD";
    private const string WorkspaceId      = "ws-123";
    private const int    Minutes          = 30;

    // ─────────────────────────────────────────────────────────────────────────
    // Happy-path (tool succeeds)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_Success_ReturnsCompletedWithCitations()
    {
        // Arrange
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        repoMock
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentRun);
        repoMock
            .Setup(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.CompleteRunAsync(
                agentRun.RunId,
                AgentRunStatus.Completed,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kqlResponse = new KqlToolResponse(
            Ok:            true,
            Rows:          new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["message"] = "error: NullReference" }
            },
            ExecutedQuery: "search * | where TimeGenerated > ago(30m) | take 20",
            WorkspaceId:   WorkspaceId,
            Timespan:      "PT30M",
            ExecutedAtUtc: DateTimeOffset.UtcNow,
            Error:         null);

        var kqlMock = new Mock<IKqlToolClient>(MockBehavior.Strict);
        kqlMock
            .Setup(k => k.ExecuteAsync(It.IsAny<KqlToolRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(kqlResponse);

        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert
        Assert.Equal(agentRun.RunId, result.RunId);
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.NotEmpty(result.Citations);
        Assert.Equal(WorkspaceId, result.Citations[0].WorkspaceId);
        Assert.NotEmpty(result.RunbookCitations);

        // Verify tool calls: KQL + runbook
        repoMock.Verify(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        repoMock.Verify(r => r.CompleteRunAsync(
            agentRun.RunId, AgentRunStatus.Completed,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        // Policy events: session + KQL allowlist + budget + runbook allowlist + budget (all allowed)
        repoMock.Verify(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(5));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tool failure path (KQL client throws)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ToolThrows_ReturnsDegradedAndStillPersistsToolCall()
    {
        // Arrange
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        repoMock
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentRun);
        repoMock
            .Setup(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.CompleteRunAsync(
                agentRun.RunId,
                AgentRunStatus.Degraded,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kqlMock = new Mock<IKqlToolClient>(MockBehavior.Strict);
        kqlMock
            .Setup(k => k.ExecuteAsync(It.IsAny<KqlToolRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused to McpHost"));

        var runbookMock = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);
        var (allowlist, budget, _) = CreateAllowAllGovernanceMocks();
        // HttpRequestException → TOOL_HTTP_ERROR, IsDegraded=true
        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);
        degraded
            .Setup(d => d.MapFailure(It.IsAny<Exception>()))
            .Returns(new DegradedDecision(true, "TOOL_HTTP_ERROR", "Tool connectivity failure", true));

        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert: degraded but still has KQL citations (evidence of failure); runbook never ran
        Assert.Equal(agentRun.RunId, result.RunId);
        Assert.Equal(AgentRunStatus.Degraded, result.Status);
        Assert.Null(result.SummaryJson);
        Assert.NotEmpty(result.Citations);
        Assert.Empty(result.RunbookCitations);

        // The tool call must still be persisted even on failure (audit trail)
        repoMock.Verify(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()), Times.Once);
        repoMock.Verify(r => r.CompleteRunAsync(
            agentRun.RunId, AgentRunStatus.Degraded,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        // session + allowlist + budget + degraded = 4 policy events (runbook never reached)
        repoMock.Verify(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tool returns Ok=false (partial failure via McpHost)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ToolReturnsNotOk_ReturnsDegradedStatus()
    {
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        repoMock
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentRun);
        repoMock
            .Setup(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.CompleteRunAsync(
                agentRun.RunId,
                AgentRunStatus.Degraded,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kqlMock = new Mock<IKqlToolClient>(MockBehavior.Strict);
        kqlMock
            .Setup(k => k.ExecuteAsync(It.IsAny<KqlToolRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KqlToolResponse(
                Ok:            false,
                Rows:          Array.Empty<IReadOnlyDictionary<string, object?>>(),
                ExecutedQuery: "...",
                WorkspaceId:   WorkspaceId,
                Timespan:      "PT30M",
                ExecutedAtUtc: DateTimeOffset.UtcNow,
                Error:         "Query timed out"));

        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System);

        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        Assert.Equal(AgentRunStatus.Degraded, result.Status);
        repoMock.Verify(r => r.CompleteRunAsync(
            agentRun.RunId, AgentRunStatus.Degraded,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Structured-logging verification
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_Success_LogsStartAndCompletion()
    {
        // Arrange
        var agentRun    = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock    = CreateHappyPathRepo(agentRun);
        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var logMock     = new Mock<ILogger<TriageOrchestrator>>();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object, logMock.Object,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System);

        // Act
        await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert: two Information logs (start + completion), zero Warning logs
        logMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Triage run starting")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        logMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("completed with status")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        logMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_ToolThrows_LogsStartAndWarning()
    {
        // Arrange
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        repoMock
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentRun);
        repoMock
            .Setup(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.CompleteRunAsync(
                agentRun.RunId, AgentRunStatus.Degraded,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kqlMock = new Mock<IKqlToolClient>(MockBehavior.Strict);
        kqlMock
            .Setup(k => k.ExecuteAsync(It.IsAny<KqlToolRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("KQL timeout"));

        var runbookMock = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);
        var logMock = new Mock<ILogger<TriageOrchestrator>>();

        var (allowlist, budget, _) = CreateAllowAllGovernanceMocks();
        // InvalidOperationException → UNKNOWN_FAILURE, IsDegraded=true
        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);
        degraded
            .Setup(d => d.MapFailure(It.IsAny<Exception>()))
            .Returns(new DegradedDecision(true, "UNKNOWN_FAILURE", "An unexpected error occurred", false));

        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object, logMock.Object,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System);

        // Act
        await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert: one start log, one warning (with exception), NO completion log
        logMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Triage run starting")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        logMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("KQL tool threw")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // The completion log should NOT appear — the exception path returns early
        logMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("completed with status")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Governance policy tests (Slice 3A)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ToolDeniedByAllowlist_ReturnsFailedAndNoMcpCall()
    {
        // Arrange
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        repoMock
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentRun);
        repoMock
            .Setup(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.CompleteRunAsync(
                agentRun.RunId, AgentRunStatus.Failed,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kqlMock = new Mock<IKqlToolClient>(MockBehavior.Strict);
        // NO setup for ExecuteAsync — will throw if called (Strict mock)

        var runbookMock = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);

        var allowlist = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        allowlist
            .Setup(a => a.CanUseTool(TenantId, "kql_query"))
            .Returns(PolicyDecision.Deny("TOOL_NOT_ALLOWED", "kql_query is not in the tenant allowlist"));

        var budget = new Mock<ITokenBudgetPolicy>(MockBehavior.Strict);
        // budget should NOT be called — allowlist short-circuits first
        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert
        Assert.Equal(agentRun.RunId, result.RunId);
        Assert.Equal(AgentRunStatus.Failed, result.Status);
        Assert.Empty(result.Citations);
        Assert.Empty(result.RunbookCitations);

        // MCP was NEVER called
        kqlMock.Verify(k => k.ExecuteAsync(It.IsAny<KqlToolRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        // 2 policy events: session + allowlist deny
        repoMock.Verify(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_BudgetDenied_ReturnsFailedAndNoMcpCall()
    {
        // Arrange
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        repoMock
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentRun);
        repoMock
            .Setup(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.CompleteRunAsync(
                agentRun.RunId, AgentRunStatus.Failed,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kqlMock = new Mock<IKqlToolClient>(MockBehavior.Strict);
        // NO setup for ExecuteAsync — will throw if called (Strict mock)

        var runbookMock = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);

        var allowlist = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        allowlist
            .Setup(a => a.CanUseTool(TenantId, "kql_query"))
            .Returns(PolicyDecision.Allow());

        var budget = new Mock<ITokenBudgetPolicy>(MockBehavior.Strict);
        budget
            .Setup(b => b.CheckRunBudget(TenantId, It.IsAny<Guid>()))
            .Returns(BudgetDecision.Deny("BUDGET_EXCEEDED", "Tenant monthly token budget exhausted"));

        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert
        Assert.Equal(agentRun.RunId, result.RunId);
        Assert.Equal(AgentRunStatus.Failed, result.Status);
        Assert.Empty(result.Citations);
        Assert.Empty(result.RunbookCitations);

        // MCP was NEVER called
        kqlMock.Verify(k => k.ExecuteAsync(It.IsAny<KqlToolRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        // 3 policy events: session + allowlist (allow) + budget (deny)
        repoMock.Verify(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task RunAsync_ToolThrows_DegradedPolicyMapsToFailedStatus()
    {
        // Arrange — degraded policy returns IsDegraded=false → maps to Failed
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        repoMock
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentRun);
        repoMock
            .Setup(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.CompleteRunAsync(
                agentRun.RunId, AgentRunStatus.Failed,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kqlMock = new Mock<IKqlToolClient>(MockBehavior.Strict);
        kqlMock
            .Setup(k => k.ExecuteAsync(It.IsAny<KqlToolRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("No access"));

        var runbookMock = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);
        var (allowlist, budget, _) = CreateAllowAllGovernanceMocks();
        // UnauthorizedAccessException → AUTH_FAILURE, IsDegraded=false (→ Failed)
        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);
        degraded
            .Setup(d => d.MapFailure(It.IsAny<Exception>()))
            .Returns(new DegradedDecision(false, "AUTH_FAILURE", "Authentication failed", false));

        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert: IsDegraded=false → maps to Failed, NOT Degraded
        Assert.Equal(agentRun.RunId, result.RunId);
        Assert.Equal(AgentRunStatus.Failed, result.Status);
        Assert.NotEmpty(result.Citations);
        Assert.Empty(result.RunbookCitations);

        // 4 policy events: session + allowlist + budget + degraded
        repoMock.Verify(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
        repoMock.Verify(r => r.CompleteRunAsync(
            agentRun.RunId, AgentRunStatus.Failed,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Runbook-specific tests (Slice 3B)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_RunbookAllowlistDenied_StillCompletes_EmptyRunbookCitations()
    {
        // Arrange — KQL succeeds, but runbook_search is not in the allowlist
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = CreateHappyPathRepo(agentRun);
        var kqlMock  = CreateHappyPathKql();

        var allowlist = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        allowlist.Setup(a => a.CanUseTool(TenantId, "kql_query")).Returns(PolicyDecision.Allow());
        allowlist.Setup(a => a.CanUseTool(TenantId, "runbook_search"))
                 .Returns(PolicyDecision.Deny("TOOL_NOT_ALLOWED", "runbook_search is not in the tenant allowlist"));

        var budget = new Mock<ITokenBudgetPolicy>(MockBehavior.Strict);
        budget.Setup(b => b.CheckRunBudget(TenantId, It.IsAny<Guid>())).Returns(BudgetDecision.Allow());

        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);
        var runbookMock = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);
        // NO ExecuteAsync setup — must NOT be called
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.NotEmpty(result.Citations);
        Assert.Empty(result.RunbookCitations);

        // 1 KQL tool call, NO runbook tool call
        repoMock.Verify(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()), Times.Once);
        // 4 policy events: session + kql_allow + budget_allow + runbook_deny
        repoMock.Verify(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Fact]
    public async Task RunAsync_RunbookBudgetDenied_StillCompletes_EmptyRunbookCitations()
    {
        // Arrange — KQL succeeds, runbook allowlist allows, but budget denies runbook
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = CreateHappyPathRepo(agentRun);
        var kqlMock  = CreateHappyPathKql();

        var allowlist = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        allowlist.Setup(a => a.CanUseTool(TenantId, "kql_query")).Returns(PolicyDecision.Allow());
        allowlist.Setup(a => a.CanUseTool(TenantId, "runbook_search")).Returns(PolicyDecision.Allow());

        // Budget allows first check (KQL path), then denies second check (runbook path)
        var budget = new Mock<ITokenBudgetPolicy>(MockBehavior.Strict);
        var budgetCallCount = 0;
        budget.Setup(b => b.CheckRunBudget(TenantId, It.IsAny<Guid>()))
              .Returns(() =>
              {
                  budgetCallCount++;
                  return budgetCallCount == 1
                      ? BudgetDecision.Allow()
                      : BudgetDecision.Deny("BUDGET_EXCEEDED", "Tenant monthly token budget exhausted");
              });

        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);
        var runbookMock = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);
        // NO ExecuteAsync setup — must NOT be called
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.NotEmpty(result.Citations);
        Assert.Empty(result.RunbookCitations);

        // 1 KQL tool call, NO runbook tool call
        repoMock.Verify(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()), Times.Once);
        // 5 policy events: session + kql_allow + budget_allow + runbook_allow + runbook_budget_deny
        repoMock.Verify(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(5));
    }

    [Fact]
    public async Task RunAsync_RunbookThrows_PartialDegradation_KqlCitationsPreserved()
    {
        // Arrange — KQL succeeds, runbook tool throws → partial degradation,
        //           KQL citations preserved, runbook citations empty
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = CreateHappyPathRepo(agentRun);
        var kqlMock  = CreateHappyPathKql();

        var runbookMock = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);
        runbookMock
            .Setup(r => r.ExecuteAsync(It.IsAny<RunbookSearchToolRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Runbook MCP process crashed"));

        var (allowlist, budget, _) = CreateAllowAllGovernanceMocks();

        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);
        degraded.Setup(d => d.MapFailure(It.IsAny<Exception>()))
                .Returns(new DegradedDecision(true, "UNKNOWN_FAILURE", "Unexpected error", false));

        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert: run still completes (KQL succeeded), but runbook citations are empty
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.NotEmpty(result.Citations);               // KQL citations preserved
        Assert.Empty(result.RunbookCitations);           // Runbook failed → no citations

        // 2 tool calls: kql (success) + runbook (failed)
        repoMock.Verify(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        // 6 policy events: session + kql_allow + budget_allow + runbook_allow + runbook_budget_allow + runbook_degraded
        repoMock.Verify(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(6));
    }

    [Fact]
    public async Task RunAsync_BothToolsSucceed_SummaryIncludesRunbookHitCount()
    {
        // Arrange — both KQL and runbook succeed → verify summary JSON includes runbookHits
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock    = CreateHappyPathRepo(agentRun);
        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.NotEmpty(result.Citations);
        Assert.Single(result.RunbookCitations);
        Assert.Equal("high-cpu", result.RunbookCitations[0].RunbookId);
        Assert.Equal("High CPU Troubleshooting", result.RunbookCitations[0].Title);

        // Summary JSON includes runbook hit count
        Assert.NotNull(result.SummaryJson);
        Assert.Contains("runbookHits", result.SummaryJson);
        Assert.Contains("1", result.SummaryJson); // 1 runbook hit
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Session continuity tests (Dev Slice 4B)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SessionResume_WithPriorRuns_SetsUsedSessionContext()
    {
        // Arrange
        var existingSessionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var existingSession = new SessionInfo(existingSessionId, TenantId, now.AddMinutes(-10), now.AddMinutes(20), false);
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint, existingSessionId);
        var priorRun = AgentRun.Create(TenantId, AlertFingerprint, existingSessionId);

        var repoMock = CreateHappyPathRepo(agentRun);
        repoMock
            .Setup(r => r.GetRecentRunsBySessionAsync(existingSessionId, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentRun> { priorRun });

        var kqlMock = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();

        var sessionPolicy = new Mock<ISessionPolicy>(MockBehavior.Strict);
        sessionPolicy
            .Setup(p => p.GetSessionTtl(It.IsAny<string>()))
            .Returns(TimeSpan.FromMinutes(30));

        var sessionStore = new Mock<ISessionStore>(MockBehavior.Strict);
        sessionStore
            .Setup(s => s.GetIncludingExpiredAsync(existingSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSession);
        sessionStore
            .Setup(s => s.TouchAsync(existingSessionId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var timeMock = new Mock<TimeProvider>(MockBehavior.Strict);
        timeMock.Setup(t => t.GetUtcNow()).Returns(now);

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object, timeMock.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes, sessionId: existingSessionId);

        // Assert
        Assert.True(result.UsedSessionContext);
        Assert.False(result.IsNewSession);
        Assert.Equal(existingSessionId, result.SessionId);
        sessionStore.Verify(s => s.TouchAsync(existingSessionId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SessionResume_TenantMismatch_ThrowsSessionTenantMismatchException()
    {
        // Arrange
        var existingSessionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var ownerTenant = "tenant-OTHER";
        var existingSession = new SessionInfo(existingSessionId, ownerTenant, now.AddMinutes(-10), now.AddMinutes(20), false);

        // No repo needed — exception is thrown before CreateRunAsync
        var repoMock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        var kqlMock = new Mock<IKqlToolClient>(MockBehavior.Strict);
        var runbookMock = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();

        var sessionPolicy = new Mock<ISessionPolicy>(MockBehavior.Strict);
        sessionPolicy
            .Setup(p => p.GetSessionTtl(It.IsAny<string>()))
            .Returns(TimeSpan.FromMinutes(30));

        var sessionStore = new Mock<ISessionStore>(MockBehavior.Strict);
        sessionStore
            .Setup(s => s.GetIncludingExpiredAsync(existingSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSession);

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<SessionTenantMismatchException>(
            () => sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes, sessionId: existingSessionId));

        Assert.Equal(existingSessionId, ex.SessionId);
        Assert.Equal(ownerTenant, ex.OwnerTenantId);
        Assert.Equal(TenantId, ex.CallerTenantId);
    }

    [Fact]
    public async Task SessionExpired_CreatesNewSession()
    {
        // Arrange
        var expiredSessionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var expiredSession = new SessionInfo(expiredSessionId, TenantId, now.AddMinutes(-60), now.AddMinutes(-5), false);
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = CreateHappyPathRepo(agentRun);
        var kqlMock = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();

        var newSessionId = Guid.NewGuid();
        var sessionPolicy = new Mock<ISessionPolicy>(MockBehavior.Strict);
        sessionPolicy
            .Setup(p => p.GetSessionTtl(It.IsAny<string>()))
            .Returns(TimeSpan.FromMinutes(30));

        var sessionStore = new Mock<ISessionStore>(MockBehavior.Strict);
        sessionStore
            .Setup(s => s.GetIncludingExpiredAsync(expiredSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredSession);
        sessionStore
            .Setup(s => s.CreateAsync(TenantId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tenantId, TimeSpan ttl, CancellationToken _) =>
                new SessionInfo(newSessionId, tenantId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.Add(ttl), true));

        var timeMock = new Mock<TimeProvider>(MockBehavior.Strict);
        timeMock.Setup(t => t.GetUtcNow()).Returns(now);

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object, timeMock.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes, sessionId: expiredSessionId);

        // Assert
        Assert.True(result.IsNewSession);
        Assert.NotEqual(expiredSessionId, result.SessionId);
        Assert.Equal(newSessionId, result.SessionId);
    }

    [Fact]
    public async Task SessionNotFound_CreatesNewSession()
    {
        // Arrange
        var unknownSessionId = Guid.NewGuid();
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = CreateHappyPathRepo(agentRun);
        var kqlMock = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();

        var sessionPolicy = new Mock<ISessionPolicy>(MockBehavior.Strict);
        sessionPolicy
            .Setup(p => p.GetSessionTtl(It.IsAny<string>()))
            .Returns(TimeSpan.FromMinutes(30));

        var sessionStore = new Mock<ISessionStore>(MockBehavior.Strict);
        sessionStore
            .Setup(s => s.GetIncludingExpiredAsync(unknownSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionInfo?)null);
        sessionStore
            .Setup(s => s.CreateAsync(TenantId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tenantId, TimeSpan ttl, CancellationToken _) =>
                new SessionInfo(Guid.NewGuid(), tenantId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.Add(ttl), true));

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes, sessionId: unknownSessionId);

        // Assert
        Assert.True(result.IsNewSession);
        sessionStore.Verify(s => s.CreateAsync(TenantId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoSessionId_CreatesNewSession_UsedSessionContextFalse()
    {
        // Arrange
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = CreateHappyPathRepo(agentRun);
        var kqlMock = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert
        Assert.True(result.IsNewSession);
        Assert.False(result.UsedSessionContext);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers — shared mock setup
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

    /// <summary>
    /// Creates governance mocks that allow everything — used as the
    /// default for existing tests where governance is not under test.
    /// </summary>
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

    /// <summary>
    /// Creates session mocks that create a fresh session by default —
    /// used for tests where session behaviour is not under test.
    /// </summary>
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
