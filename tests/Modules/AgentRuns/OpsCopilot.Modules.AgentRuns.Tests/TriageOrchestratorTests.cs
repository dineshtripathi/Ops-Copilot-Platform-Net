using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Application.Acl;
using OpsCopilot.AgentRuns.Application.Options;
using OpsCopilot.AgentRuns.Application.Orchestration;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Models;
using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.BuildingBlocks.Contracts.Privacy;
using System.Net.Http;
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
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<Guid?>(), It.IsAny<RunContext?>(), It.IsAny<CancellationToken>()))
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
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System, new PermissiveRunbookAclFilter());

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
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<Guid?>(), It.IsAny<RunContext?>(), It.IsAny<CancellationToken>()))
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
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System, new PermissiveRunbookAclFilter());

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
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<Guid?>(), It.IsAny<RunContext?>(), It.IsAny<CancellationToken>()))
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
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System, new PermissiveRunbookAclFilter());

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
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System, new PermissiveRunbookAclFilter());

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
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<Guid?>(), It.IsAny<RunContext?>(), It.IsAny<CancellationToken>()))
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
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System, new PermissiveRunbookAclFilter());

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
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<Guid?>(), It.IsAny<RunContext?>(), It.IsAny<CancellationToken>()))
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
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System, new PermissiveRunbookAclFilter());

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
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<Guid?>(), It.IsAny<RunContext?>(), It.IsAny<CancellationToken>()))
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
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System, new PermissiveRunbookAclFilter());

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
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<Guid?>(), It.IsAny<RunContext?>(), It.IsAny<CancellationToken>()))
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
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System, new PermissiveRunbookAclFilter());

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
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System, new PermissiveRunbookAclFilter());

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
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System, new PermissiveRunbookAclFilter());

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
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System, new PermissiveRunbookAclFilter());

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
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System, new PermissiveRunbookAclFilter());

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
            sessionStore.Object, sessionPolicy.Object, timeMock.Object, new PermissiveRunbookAclFilter());

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes, sessionId: existingSessionId);

        // Assert
        Assert.True(result.UsedSessionContext);
        Assert.False(result.IsNewSession);
        Assert.Equal(existingSessionId, result.SessionId);
        Assert.Equal("SessionResumed", result.SessionReasonCode);
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
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System, new PermissiveRunbookAclFilter());

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
            sessionStore.Object, sessionPolicy.Object, timeMock.Object, new PermissiveRunbookAclFilter());

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes, sessionId: expiredSessionId);

        // Assert
        Assert.True(result.IsNewSession);
        Assert.NotEqual(expiredSessionId, result.SessionId);
        Assert.Equal(newSessionId, result.SessionId);
        Assert.Equal("SessionExpiredFallback", result.SessionReasonCode);
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
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System, new PermissiveRunbookAclFilter());

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes, sessionId: unknownSessionId);

        // Assert
        Assert.True(result.IsNewSession);
        Assert.Equal("SessionNotFoundFallback", result.SessionReasonCode);
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
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System, new PermissiveRunbookAclFilter());

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert
        Assert.True(result.IsNewSession);
        Assert.False(result.UsedSessionContext);
        Assert.Equal("SessionCreated", result.SessionReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers — shared mock setup
    // ─────────────────────────────────────────────────────────────────────────

    private static Mock<IAgentRunRepository> CreateHappyPathRepo(AgentRun agentRun)
    {
        var mock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        mock.Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<Guid?>(), It.IsAny<RunContext?>(), It.IsAny<CancellationToken>()))
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

    // ── Dev Slice 56 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_WithChatClient_PopulatesLedgerFields()
    {
        // Arrange
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = CreateHappyPathRepo(agentRun);
        repoMock
            .Setup(r => r.UpdateRunLedgerAsync(
                agentRun.RunId,
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Analysis complete"))
        {
            Usage = new UsageDetails
            {
                InputTokenCount  = 10,
                OutputTokenCount = 20,
                TotalTokenCount  = 30
            }
        };

        var chatClientMock = new Mock<IChatClient>(MockBehavior.Strict);
        chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);

        var modelRoutingMock = new Mock<IModelRoutingPolicy>(MockBehavior.Strict);
        modelRoutingMock
            .Setup(m => m.SelectModelAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelDescriptor("gpt-4o"));

        var promptVersionMock = new Mock<IPromptVersionService>(MockBehavior.Strict);
        promptVersionMock
            .Setup(p => p.GetCurrentVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromptVersionInfo("1.0.0", "Analyze the situation."));

        var sut = new TriageOrchestrator(
            repoMock.Object,
            kqlMock.Object,
            runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object,
            budget.Object,
            degraded.Object,
            sessionStore.Object,
            sessionPolicy.Object,
            TimeProvider.System,
            new PermissiveRunbookAclFilter(),
            chatClientMock.Object,
            modelRoutingMock.Object,
            promptVersionMock.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Equal("gpt-4o", result.ModelId);
        Assert.Equal("1.0.0", result.PromptVersionId);
        Assert.Equal(10, result.InputTokens);
        Assert.Equal(20, result.OutputTokens);
        Assert.Equal(30, result.TotalTokens);
        Assert.NotNull(result.EstimatedCost);

        repoMock.Verify(
            r => r.UpdateRunLedgerAsync(
                agentRun.RunId,
                "gpt-4o",
                "1.0.0",
                10,
                20,
                30,
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_ChatClientThrows_ReturnsDegraded_UpdateLedgerNotCalled()
    {
        // Arrange
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = CreateHappyPathRepo(agentRun);
        repoMock
            .Setup(r => r.CompleteRunAsync(
                agentRun.RunId, AgentRunStatus.Degraded,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // No UpdateRunLedgerAsync setup — it must never be called.

        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var chatClientMock = new Mock<IChatClient>(MockBehavior.Strict);
        chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("LLM unavailable"));

        var modelRoutingMock = new Mock<IModelRoutingPolicy>(MockBehavior.Strict);
        modelRoutingMock
            .Setup(m => m.SelectModelAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelDescriptor("gpt-4o"));

        var promptVersionMock = new Mock<IPromptVersionService>(MockBehavior.Strict);
        promptVersionMock
            .Setup(p => p.GetCurrentVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromptVersionInfo("1.0.0", "Analyze the situation."));

        var sut = new TriageOrchestrator(
            repoMock.Object,
            kqlMock.Object,
            runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object,
            budget.Object,
            degraded.Object,
            sessionStore.Object,
            sessionPolicy.Object,
            TimeProvider.System,
            new PermissiveRunbookAclFilter(),
            chatClientMock.Object,
            modelRoutingMock.Object,
            promptVersionMock.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert — run completes as Degraded; ledger never written
        Assert.Equal(AgentRunStatus.Degraded, result.Status);
        Assert.Null(result.ModelId);
        Assert.Null(result.InputTokens);

        repoMock.Verify(
            r => r.UpdateRunLedgerAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_LlmSucceeds_WritesExactCostToLedger_ForGpt4o()
    {
        // Arrange — 1M input + 1M output for gpt-4o → $2.50 + $10.00 = $12.50
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock = CreateHappyPathRepo(agentRun);
        repoMock
            .Setup(r => r.UpdateRunLedgerAsync(
                agentRun.RunId,
                "gpt-4o",
                "1.0.0",
                1_000_000,
                1_000_000,
                2_000_000,
                12.50m,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Cost test analysis"))
        {
            Usage = new UsageDetails
            {
                InputTokenCount  = 1_000_000,
                OutputTokenCount = 1_000_000,
                TotalTokenCount  = 2_000_000
            }
        };

        var chatClientMock = new Mock<IChatClient>(MockBehavior.Strict);
        chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);

        var modelRoutingMock = new Mock<IModelRoutingPolicy>(MockBehavior.Strict);
        modelRoutingMock
            .Setup(m => m.SelectModelAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelDescriptor("gpt-4o"));

        var promptVersionMock = new Mock<IPromptVersionService>(MockBehavior.Strict);
        promptVersionMock
            .Setup(p => p.GetCurrentVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromptVersionInfo("1.0.0", "Analyze the situation."));

        var sut = new TriageOrchestrator(
            repoMock.Object,
            kqlMock.Object,
            runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object,
            budget.Object,
            degraded.Object,
            sessionStore.Object,
            sessionPolicy.Object,
            TimeProvider.System,
            new PermissiveRunbookAclFilter(),
            chatClientMock.Object,
            modelRoutingMock.Object,
            promptVersionMock.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert — exact cost for gpt-4o at 1M/1M tokens
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Equal("gpt-4o", result.ModelId);
        Assert.Equal(12.50m,   result.EstimatedCost);

        repoMock.Verify(
            r => r.UpdateRunLedgerAsync(
                agentRun.RunId,
                "gpt-4o",
                "1.0.0",
                1_000_000,
                1_000_000,
                2_000_000,
                12.50m,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_WorkspaceScopeDenied_ReturnsFailedAndNoKqlCall()
    {
        // Arrange — scope evaluator denies; KQL must never be called
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = CreateHappyPathRepo(agentRun);
        repoMock
            .Setup(r => r.CompleteRunAsync(
                agentRun.RunId, AgentRunStatus.Failed,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Strict mocks with NO setup — must NOT be called
        var kqlMock     = new Mock<IKqlToolClient>(MockBehavior.Strict);
        var runbookMock = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var scopeEvaluator = new Mock<ITargetScopeEvaluator>(MockBehavior.Strict);
        scopeEvaluator
            .Setup(x => x.Evaluate(TenantId, "log_analytics_workspace", WorkspaceId))
            .Returns(TargetScopeDecision.Deny("WORKSPACE_NOT_ALLOWED",
                $"Workspace '{WorkspaceId}' is not in the tenant's approved workspace list."));

        var sut = new TriageOrchestrator(
            repoMock.Object,
            kqlMock.Object,
            runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object,
            budget.Object,
            degraded.Object,
            sessionStore.Object,
            sessionPolicy.Object,
            TimeProvider.System,
            new PermissiveRunbookAclFilter(),
            scopeEvaluator: scopeEvaluator.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert
        Assert.Equal(AgentRunStatus.Failed, result.Status);

        // KQL and runbook must never be reached (strict mocks enforce this)
        kqlMock.VerifyNoOtherCalls();
        runbookMock.VerifyNoOtherCalls();

        // Scope-deny policy event must have been appended exactly once
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
    // Slice 60 — deployment_diff MCP tool integration
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NullSubscriptionId_SkipsDeploymentDiff()
    {
        // Arrange
        var agentRun    = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock    = CreateHappyPathRepo(agentRun);
        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        // Strict mock — any call to ExecuteAsync would throw (no setup)
        var ddMock = new Mock<IDeploymentDiffToolClient>(MockBehavior.Strict);

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, new PermissiveRunbookAclFilter(),
            deploymentDiff: ddMock.Object);

        // Act — subscriptionId omitted → null → guard fails, dd block skipped
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Empty(result.DeploymentDiffCitations);
        repoMock.Verify(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(),
            It.IsAny<CancellationToken>()), Times.Exactly(5));
        ddMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_NullDeploymentDiffClient_SkipsBlock()
    {
        // Arrange — client not wired at all (defaults to null)
        var agentRun    = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock    = CreateHappyPathRepo(agentRun);
        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, new PermissiveRunbookAclFilter());
        // deploymentDiff defaults to null

        // Act — subscriptionId provided, but client is null → guard fails
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes,
            subscriptionId: "sub-abc123");

        // Assert
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Empty(result.DeploymentDiffCitations);
        repoMock.Verify(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(),
            It.IsAny<CancellationToken>()), Times.Exactly(5));
    }

    [Fact]
    public async Task RunAsync_DeploymentDiffAllowlistDenied_DoesNotCallClient()
    {
        // Arrange
        var agentRun    = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock    = CreateHappyPathRepo(agentRun);
        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (_, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        // Allow all tools EXCEPT deployment_diff (last matching setup wins in Moq)
        var allowlist = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        allowlist.Setup(a => a.CanUseTool(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(PolicyDecision.Allow());
        allowlist.Setup(a => a.CanUseTool(It.IsAny<string>(), "deployment_diff"))
            .Returns(PolicyDecision.Deny("TOOL_NOT_ALLOWED", "deployment_diff is not in the tenant allowlist"));

        var ddMock = new Mock<IDeploymentDiffToolClient>(MockBehavior.Strict);

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, new PermissiveRunbookAclFilter(),
            deploymentDiff: ddMock.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes,
            subscriptionId: "sub-abc123");

        // Assert — run completes but no citations; allowlist policy event counted
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Empty(result.DeploymentDiffCitations);
        // 5 base + 1 DD allowlist = 6
        repoMock.Verify(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(),
            It.IsAny<CancellationToken>()), Times.Exactly(6));
        ddMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_DeploymentDiffBudgetDenied_DoesNotCallClient()
    {
        // Arrange
        var agentRun    = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock    = CreateHappyPathRepo(agentRun);
        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, _, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        // KQL budget allow, runbook budget allow, dd budget deny (sequence)
        var budget = new Mock<ITokenBudgetPolicy>(MockBehavior.Strict);
        budget.SetupSequence(b => b.CheckRunBudget(It.IsAny<string>(), It.IsAny<Guid>()))
            .Returns(BudgetDecision.Allow())
            .Returns(BudgetDecision.Allow())
            .Returns(BudgetDecision.Deny("BUDGET_EXCEEDED", "Tenant monthly token budget exhausted"));

        var ddMock = new Mock<IDeploymentDiffToolClient>(MockBehavior.Strict);

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, new PermissiveRunbookAclFilter(),
            deploymentDiff: ddMock.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes,
            subscriptionId: "sub-abc123");

        // Assert — run completes; budget exhausted stops the dd call
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Empty(result.DeploymentDiffCitations);
        // 5 base + 1 DD allowlist + 1 DD budget = 7
        repoMock.Verify(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(),
            It.IsAny<CancellationToken>()), Times.Exactly(7));
        ddMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_DeploymentDiffSuccess_PopulatesCitations()
    {
        // Arrange
        var agentRun    = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock    = CreateHappyPathRepo(agentRun);
        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var ddMock = new Mock<IDeploymentDiffToolClient>();
        ddMock.Setup(d => d.ExecuteAsync(It.IsAny<DeploymentDiffRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeploymentDiffResponse(
                Ok: true,
                Changes: new List<DeploymentDiffChange>
                {
                    new("res-001", "rg-prod", "Create", DateTimeOffset.UtcNow, "Created vnet"),
                    new("res-002", "rg-prod", "Delete", DateTimeOffset.UtcNow, "Deleted NIC")
                },
                SubscriptionId: "sub-abc123",
                ExecutedAtUtc: DateTimeOffset.UtcNow));

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, new PermissiveRunbookAclFilter(),
            deploymentDiff: ddMock.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes,
            subscriptionId: "sub-abc123");

        // Assert
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Equal(2, result.DeploymentDiffCitations.Count);
        Assert.Equal("sub-abc123", result.DeploymentDiffCitations[0].SubscriptionId);
        Assert.Equal("res-001", result.DeploymentDiffCitations[0].ResourceId);
        Assert.Equal("res-002", result.DeploymentDiffCitations[1].ResourceId);
        // 5 base + 1 DD allowlist + 1 DD budget = 7
        repoMock.Verify(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(),
            It.IsAny<CancellationToken>()), Times.Exactly(7));
    }

    [Fact]
    public async Task RunAsync_DeploymentDiffReturnsNotOk_EmptyCitations()
    {
        // Arrange
        var agentRun    = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock    = CreateHappyPathRepo(agentRun);
        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var ddMock = new Mock<IDeploymentDiffToolClient>();
        ddMock.Setup(d => d.ExecuteAsync(It.IsAny<DeploymentDiffRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeploymentDiffResponse(
                Ok: false,
                Changes: new List<DeploymentDiffChange>(),
                SubscriptionId: "sub-abc123",
                ExecutedAtUtc: DateTimeOffset.UtcNow,
                Error: "ARM query failed"));

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, new PermissiveRunbookAclFilter(),
            deploymentDiff: ddMock.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes,
            subscriptionId: "sub-abc123");

        // Assert — Ok=false means no citations but run still completes
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Empty(result.DeploymentDiffCitations);
        // ToolCallAsync still appended (success code path for dd)
        repoMock.Verify(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3)); // KQL + runbook + dd
        // 5 base + 1 DD allowlist + 1 DD budget = 7
        repoMock.Verify(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(),
            It.IsAny<CancellationToken>()), Times.Exactly(7));
    }

    [Fact]
    public async Task RunAsync_DeploymentDiffThrows_PartialDegradation_RunCompletesWithEmptyCitations()
    {
        // Arrange
        var agentRun    = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock    = CreateHappyPathRepo(agentRun);
        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var ddMock = new Mock<IDeploymentDiffToolClient>();
        ddMock.Setup(d => d.ExecuteAsync(It.IsAny<DeploymentDiffRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("ARM endpoint timeout"));

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, new PermissiveRunbookAclFilter(),
            deploymentDiff: ddMock.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes,
            subscriptionId: "sub-abc123");

        // Assert — partial degradation: dd failure does NOT fail the overall run
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Empty(result.DeploymentDiffCitations);
        // degraded.MapFailure must have been called
        degraded.Verify(d => d.MapFailure(It.IsAny<Exception>()), Times.Once);
        // 5 base + 1 DD allowlist + 1 DD budget + 1 DD degraded = 8
        repoMock.Verify(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(),
            It.IsAny<CancellationToken>()), Times.Exactly(8));
    }

    [Fact]
    public async Task RunAsync_DeploymentDiffSuccess_SummaryJsonContainsDiffHits()
    {
        // Arrange
        var agentRun    = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock    = CreateHappyPathRepo(agentRun);
        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var ddMock = new Mock<IDeploymentDiffToolClient>();
        ddMock.Setup(d => d.ExecuteAsync(It.IsAny<DeploymentDiffRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeploymentDiffResponse(
                Ok: true,
                Changes: new List<DeploymentDiffChange>
                {
                    new("res-001", "rg-prod", "Create", DateTimeOffset.UtcNow, "Created vnet"),
                    new("res-002", "rg-prod", "Delete", DateTimeOffset.UtcNow, "Deleted NIC")
                },
                SubscriptionId: "sub-abc123",
                ExecutedAtUtc: DateTimeOffset.UtcNow));

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, new PermissiveRunbookAclFilter(),
            deploymentDiff: ddMock.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes,
            subscriptionId: "sub-abc123");

        // Assert — summaryJson must include diffHits=2
        Assert.NotNull(result.SummaryJson);
        using var doc = System.Text.Json.JsonDocument.Parse(result.SummaryJson!);
        Assert.Equal(2, doc.RootElement.GetProperty("diffHits").GetInt32());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Slice 105: RunContext propagation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_WithContext_ContextPassedToCreateRunAsync()
    {
        // Arrange
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var capturedContext = (RunContext?)null;
        var repoMock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        repoMock
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<Guid?>(), It.IsAny<RunContext?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Guid?, RunContext?, CancellationToken>((_, _, _, ctx, _) => capturedContext = ctx)
            .ReturnsAsync(agentRun);
        repoMock
            .Setup(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.CompleteRunAsync(agentRun.RunId, It.IsAny<AgentRunStatus>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var context = new RunContext(
            AlertProvider:    "AzureMonitor",
            AlertSourceType:  "Metric",
            AzureResourceId:  "/subscriptions/sub-1/resourceGroups/rg-prod/providers/Microsoft.Web/sites/myapp",
            AzureApplication: "myapp",
            AzureWorkspaceId: WorkspaceId,
            AzureSubscriptionId: "sub-1",
            AzureResourceGroup:  "rg-prod");

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, new PermissiveRunbookAclFilter());

        // Act
        await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes, context: context);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal("AzureMonitor", capturedContext!.AlertProvider);
        Assert.Equal("Metric",       capturedContext.AlertSourceType);
        Assert.Equal("sub-1",        capturedContext.AzureSubscriptionId);
        Assert.Equal("rg-prod",      capturedContext.AzureResourceGroup);
        Assert.Equal("myapp",        capturedContext.AzureApplication);
        Assert.Equal(WorkspaceId,    capturedContext.AzureWorkspaceId);
    }

    // ── Dev Slice 122 — Triage Run Idempotency ───────────────────────────────

    [Fact]
    public async Task RunAsync_WithIdempotency_PendingRunExists_ReturnsDeduplicatedResult()
    {
        // Arrange — a run with the same fingerprint is already Pending
        var sessionId   = Guid.NewGuid();
        var existingRun = AgentRun.Create(TenantId, AlertFingerprint, sessionId: sessionId);
        // existingRun.Status is Pending by default

        var repoMock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        repoMock
            .Setup(r => r.FindRecentRunAsync(TenantId, AlertFingerprint, 60, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRun);
        // CreateRunAsync is NOT set up — dedup must short-circuit before it is called

        var opts = Options.Create(new IdempotencyOptions { WindowMinutes = 60 });

        var sut = new TriageOrchestrator(
            repoMock.Object,
            new Mock<IKqlToolClient>(MockBehavior.Strict).Object,
            new Mock<IRunbookSearchToolClient>(MockBehavior.Strict).Object,
            NullLogger<TriageOrchestrator>.Instance,
            new Mock<IToolAllowlistPolicy>(MockBehavior.Strict).Object,
            new Mock<ITokenBudgetPolicy>(MockBehavior.Strict).Object,
            new Mock<IDegradedModePolicy>(MockBehavior.Strict).Object,
            new Mock<ISessionStore>(MockBehavior.Strict).Object,
            new Mock<ISessionPolicy>(MockBehavior.Strict).Object,
            TimeProvider.System, new PermissiveRunbookAclFilter(),
            idempotencyOptions: opts);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert
        Assert.True(result.WasDeduplicated);
        Assert.Equal(existingRun.RunId, result.RunId);
        Assert.Equal(existingRun.Status, result.Status);
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal("DedupReused", result.SessionReasonCode);
        Assert.Empty(result.Citations);
        Assert.Empty(result.RunbookCitations);
        Assert.Empty(result.MemoryCitations);
        Assert.Empty(result.DeploymentDiffCitations);
    }

    [Fact]
    public async Task RunAsync_WithIdempotency_CompletedRunWithinWindow_ReturnsDeduplicatedResult()
    {
        // Arrange — a Completed run exists within the window
        var existingRun = AgentRun.Create(TenantId, AlertFingerprint);
        existingRun.Complete(AgentRunStatus.Completed, "{}", "[]");

        var repoMock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        repoMock
            .Setup(r => r.FindRecentRunAsync(TenantId, AlertFingerprint, 60, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRun);
        // CreateRunAsync is NOT set up — dedup must short-circuit

        var opts = Options.Create(new IdempotencyOptions { WindowMinutes = 60 });

        var sut = new TriageOrchestrator(
            repoMock.Object,
            new Mock<IKqlToolClient>(MockBehavior.Strict).Object,
            new Mock<IRunbookSearchToolClient>(MockBehavior.Strict).Object,
            NullLogger<TriageOrchestrator>.Instance,
            new Mock<IToolAllowlistPolicy>(MockBehavior.Strict).Object,
            new Mock<ITokenBudgetPolicy>(MockBehavior.Strict).Object,
            new Mock<IDegradedModePolicy>(MockBehavior.Strict).Object,
            new Mock<ISessionStore>(MockBehavior.Strict).Object,
            new Mock<ISessionPolicy>(MockBehavior.Strict).Object,
            TimeProvider.System, new PermissiveRunbookAclFilter(),
            idempotencyOptions: opts);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert
        Assert.True(result.WasDeduplicated);
        Assert.Equal(existingRun.RunId, result.RunId);
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Equal("DedupReused", result.SessionReasonCode);
    }

    [Fact]
    public async Task RunAsync_WithIdempotency_NoExistingRun_ProceedsNormally()
    {
        // Arrange — FindRecentRunAsync returns null → full triage runs
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock = CreateHappyPathRepo(agentRun);
        repoMock
            .Setup(r => r.FindRecentRunAsync(TenantId, AlertFingerprint, 60, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRun?)null);

        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();
        var opts = Options.Create(new IdempotencyOptions { WindowMinutes = 60 });

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, new PermissiveRunbookAclFilter(),
            idempotencyOptions: opts);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert — normal triage completed, NOT deduplicated
        Assert.False(result.WasDeduplicated);
        Assert.Equal(agentRun.RunId, result.RunId);
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.NotEmpty(result.Citations);
    }

    [Fact]
    public async Task RunAsync_WithIdempotency_WindowMinutesZero_SkipsGuard()
    {
        // Arrange — WindowMinutes = 0 disables dedup; FindRecentRunAsync must NOT be called
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock = CreateHappyPathRepo(agentRun);
        // FindRecentRunAsync is intentionally NOT set up on MockBehavior.Strict mock

        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();
        var opts = Options.Create(new IdempotencyOptions { WindowMinutes = 0 });

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, new PermissiveRunbookAclFilter(),
            idempotencyOptions: opts);

        // Act — must not throw (Strict mock verifies FindRecentRunAsync not called)
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert — normal triage, guard skipped
        Assert.False(result.WasDeduplicated);
        Assert.Equal(agentRun.RunId, result.RunId);
    }

    [Fact]
    public async Task RunAsync_WithIdempotencyOptionsNull_SkipsGuard()
    {
        // Arrange — no IOptions injected; FindRecentRunAsync must NOT be called
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock = CreateHappyPathRepo(agentRun);
        // FindRecentRunAsync is intentionally NOT set up on MockBehavior.Strict mock

        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, new PermissiveRunbookAclFilter()
            /* idempotencyOptions omitted → null */);

        // Act — must not throw
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert — normal triage, guard skipped
        Assert.False(result.WasDeduplicated);
        Assert.Equal(agentRun.RunId, result.RunId);
    }

    // ── Dev Slice 127 — ResumeRunAsync (dispatcher entry point) ──────────────

    [Fact]
    public async Task ResumeRunAsync_UsesExistingRunId_SkipsCreateRun()
    {
        // Arrange — repo has NO CreateRunAsync setup (Strict mock validates it is never called).
        // Only tool-call persistence and completion are expected.
        var existingRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        // CreateRunAsync intentionally omitted — must NOT be called
        repoMock
            .Setup(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.CompleteRunAsync(
                existingRun.RunId, AgentRunStatus.Completed,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // Slice 128: MarkRunningAsync is called on every dispatcher-resume path
        repoMock
            .Setup(r => r.MarkRunningAsync(existingRun.RunId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, new PermissiveRunbookAclFilter());

        // Act
        var result = await sut.ResumeRunAsync(existingRun, WorkspaceId);

        // Assert — the pre-created run's ID is preserved end-to-end
        Assert.Equal(existingRun.RunId, result.RunId);
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.False(result.WasDeduplicated);

        // Verify CreateRunAsync was never called (Strict mock would throw if called)
        repoMock.Verify(r => r.CompleteRunAsync(
            existingRun.RunId, AgentRunStatus.Completed,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResumeRunAsync_SkipsIdempotencyGuard_EvenWhenIdempotencyIsConfigured()
    {
        // Arrange — idempotency is configured but FindRecentRunAsync must NOT be called
        // because existingRun != null short-circuits the guard.
        var existingRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        // FindRecentRunAsync intentionally omitted — must NOT be called
        // CreateRunAsync intentionally omitted — must NOT be called
        repoMock
            .Setup(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.CompleteRunAsync(
                existingRun.RunId, AgentRunStatus.Completed,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // Slice 128: MarkRunningAsync is called on every dispatcher-resume path
        repoMock
            .Setup(r => r.MarkRunningAsync(existingRun.RunId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        // Configure idempotency — if the guard ran, it would call FindRecentRunAsync
        // (not set up on Strict mock → would throw). Absence of exception confirms guard bypassed.
        var opts = Options.Create(new IdempotencyOptions { WindowMinutes = 60 });

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, new PermissiveRunbookAclFilter(),
            idempotencyOptions: opts);

        // Act — must not throw even though FindRecentRunAsync is not set up
        var result = await sut.ResumeRunAsync(existingRun, WorkspaceId);

        // Assert
        Assert.Equal(existingRun.RunId, result.RunId);
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.False(result.WasDeduplicated);
    }

    // ── Dev Slice 128 — Pending → Running transition ─────────────────────────

    [Fact]
    public async Task ResumeRunAsync_MarksRunRunning_BeforePipelineStarts()
    {
        // Arrange — verifies MarkRunningAsync is called exactly once on the dispatcher-resume path.
        var existingRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        repoMock
            .Setup(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.CompleteRunAsync(
                existingRun.RunId, AgentRunStatus.Completed,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.MarkRunningAsync(existingRun.RunId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, new PermissiveRunbookAclFilter());

        // Act
        await sut.ResumeRunAsync(existingRun, WorkspaceId);

        // Assert — transition was applied exactly once before the pipeline ran
        repoMock.Verify(
            r => r.MarkRunningAsync(existingRun.RunId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_DoesNotCallMarkRunningAsync_WhenNoExistingRun()
    {
        // Arrange — normal RunAsync (dispatcher not involved) must NOT call MarkRunningAsync.
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = new Mock<IAgentRunRepository>(MockBehavior.Loose);
        repoMock
            .Setup(r => r.FindRecentRunAsync(TenantId, AlertFingerprint, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRun?)null);
        repoMock
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<Guid>(), It.IsAny<RunContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentRun);
        repoMock
            .Setup(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.CompleteRunAsync(
                agentRun.RunId, AgentRunStatus.Completed,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, new PermissiveRunbookAclFilter());

        // Act
        await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert — MarkRunningAsync must never be called in the non-dispatcher path
        repoMock.Verify(
            r => r.MarkRunningAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Dev Slice 146 — PII redaction pipeline integration ─────────────────────

    [Fact]
    public async Task RunAsync_WithPiiRedactor_LlmNarrativeIsRedacted()
    {
        // Arrange
        const string RawNarrative      = "Contact admin@contoso.com or call (555) 867-5309 for help.";
        const string RedactedNarrative = "Contact [EMAIL] or call [PHONE] for help.";

        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock = CreateHappyPathRepo(agentRun);
        repoMock.Setup(r => r.UpdateRunLedgerAsync(
            agentRun.RunId, It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, RawNarrative))
        {
            Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 20, TotalTokenCount = 30 }
        };
        var chatClientMock = new Mock<IChatClient>(MockBehavior.Strict);
        chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);

        var modelRoutingMock = new Mock<IModelRoutingPolicy>(MockBehavior.Strict);
        modelRoutingMock
            .Setup(m => m.SelectModelAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelDescriptor("gpt-4o"));

        var promptVersionMock = new Mock<IPromptVersionService>(MockBehavior.Strict);
        promptVersionMock
            .Setup(p => p.GetCurrentVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromptVersionInfo("1.0.0", "Analyze the situation."));

        var piiRedactorMock = new Mock<IPiiRedactor>(MockBehavior.Strict);
        piiRedactorMock.Setup(r => r.Redact(RawNarrative)).Returns(RedactedNarrative);

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System,
            new PermissiveRunbookAclFilter(),
            chatClientMock.Object, modelRoutingMock.Object, promptVersionMock.Object,
            piiRedactor: piiRedactorMock.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Equal(RedactedNarrative, result.LlmNarrative);
        piiRedactorMock.Verify(r => r.Redact(RawNarrative), Times.Once);
    }

    [Fact]
    public async Task RunAsync_NoPiiRedactor_LlmNarrativePassesThroughUnchanged()
    {
        // Arrange — no IPiiRedactor injected; raw narrative must not be modified
        const string RawNarrative = "Contact admin@contoso.com for info.";

        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock = CreateHappyPathRepo(agentRun);
        repoMock.Setup(r => r.UpdateRunLedgerAsync(
            agentRun.RunId, It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, RawNarrative))
        {
            Usage = new UsageDetails { InputTokenCount = 5, OutputTokenCount = 10, TotalTokenCount = 15 }
        };
        var chatClientMock = new Mock<IChatClient>(MockBehavior.Strict);
        chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);

        var modelRoutingMock = new Mock<IModelRoutingPolicy>(MockBehavior.Strict);
        modelRoutingMock
            .Setup(m => m.SelectModelAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelDescriptor("gpt-4o"));

        var promptVersionMock = new Mock<IPromptVersionService>(MockBehavior.Strict);
        promptVersionMock
            .Setup(p => p.GetCurrentVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromptVersionInfo("1.0.0", "Analyze the situation."));

        // No piiRedactor — omit parameter (defaults to null)
        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object, TimeProvider.System,
            new PermissiveRunbookAclFilter(),
            chatClientMock.Object, modelRoutingMock.Object, promptVersionMock.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert — narrative must be the raw, unredacted text
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Equal(RawNarrative, result.LlmNarrative);
    }

    // ── Dev Slice 184 — Multi-turn tool-call loop ─────────────────────────────

    [Fact]
    public async Task RunAsync_LlmRequestsFunctionCall_LoopDispatchesTool_ContinuesToFinalResponse()
    {
        // Arrange — LLM first requests kql_query tool call, then returns final text
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock = CreateHappyPathRepo(agentRun);
        repoMock.Setup(r => r.UpdateRunLedgerAsync(
                agentRun.RunId, It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        // Response 1: LLM requests kql_query tool
        var toolCallMsg = new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call-1", "kql_query",
                new Dictionary<string, object?> { ["query"] = "search *", ["timespan"] = "PT1H" })]);
        var call1Response = new ChatResponse(toolCallMsg);

        // Response 2: LLM returns final narrative after seeing tool result
        var finalMsg = new ChatMessage(ChatRole.Assistant, "Root cause: OOM");
        var call2Response = new ChatResponse(finalMsg);

        var chatClientMock = new Mock<IChatClient>(MockBehavior.Strict);
        chatClientMock
            .SetupSequence(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(call1Response)
            .ReturnsAsync(call2Response);

        var modelRoutingMock = new Mock<IModelRoutingPolicy>(MockBehavior.Strict);
        modelRoutingMock.Setup(m => m.SelectModelAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelDescriptor("gpt-4o"));

        var promptVersionMock = new Mock<IPromptVersionService>(MockBehavior.Strict);
        promptVersionMock.Setup(p => p.GetCurrentVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromptVersionInfo("1.0.0", "Analyze."));

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, new PermissiveRunbookAclFilter(),
            chatClientMock.Object, modelRoutingMock.Object, promptVersionMock.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Equal("Root cause: OOM", result.LlmNarrative);
        chatClientMock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_ToolLoop_RespectsMaxToolCallIterations()
    {
        // Arrange — MaxToolCallIterations=2; LLM always returns a tool call, loop must stop at cap
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock = CreateHappyPathRepo(agentRun);
        repoMock.Setup(r => r.UpdateRunLedgerAsync(
                agentRun.RunId, It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        // Always returns a tool call — would loop forever without the iteration cap
        var toolCallResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call-n", "kql_query",
                new Dictionary<string, object?> { ["query"] = "search *", ["timespan"] = "PT1H" })]));

        var chatClientMock = new Mock<IChatClient>(MockBehavior.Strict);
        chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(toolCallResponse);

        var modelRoutingMock = new Mock<IModelRoutingPolicy>(MockBehavior.Strict);
        modelRoutingMock.Setup(m => m.SelectModelAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelDescriptor("gpt-4o"));

        var promptVersionMock = new Mock<IPromptVersionService>(MockBehavior.Strict);
        promptVersionMock.Setup(p => p.GetCurrentVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromptVersionInfo("1.0.0", "Analyze."));

        var opts = Options.Create(new AgentLoopOptions { MaxToolCallIterations = 2 });

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, new PermissiveRunbookAclFilter(),
            chatClientMock.Object, modelRoutingMock.Object, promptVersionMock.Object,
            agentLoopOptions: opts);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert — GetResponseAsync called exactly MaxToolCallIterations=2 times, then loop exits
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        chatClientMock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_ToolLoop_UnknownToolName_ErrorFedBackToLlm_LoopCompletes()
    {
        // Arrange — LLM calls an unknown tool; error JSON is fed back; second response is final text
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock = CreateHappyPathRepo(agentRun);
        repoMock.Setup(r => r.UpdateRunLedgerAsync(
                agentRun.RunId, It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kqlMock     = CreateHappyPathKql();
        var runbookMock = CreateHappyPathRunbook();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();
        var (sessionStore, sessionPolicy) = CreateDefaultSessionMocks();

        var unknownToolCallResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call-x", "nonexistent_tool",
                new Dictionary<string, object?>())]));
        var finalResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Analysis complete"));

        var chatClientMock = new Mock<IChatClient>(MockBehavior.Strict);
        chatClientMock
            .SetupSequence(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(unknownToolCallResponse)
            .ReturnsAsync(finalResponse);

        var modelRoutingMock = new Mock<IModelRoutingPolicy>(MockBehavior.Strict);
        modelRoutingMock.Setup(m => m.SelectModelAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelDescriptor("gpt-4o"));

        var promptVersionMock = new Mock<IPromptVersionService>(MockBehavior.Strict);
        promptVersionMock.Setup(p => p.GetCurrentVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromptVersionInfo("1.0.0", "Analyze."));

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, runbookMock.Object,
            NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object,
            sessionStore.Object, sessionPolicy.Object,
            TimeProvider.System, new PermissiveRunbookAclFilter(),
            chatClientMock.Object, modelRoutingMock.Object, promptVersionMock.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert — loop completes; unknown tool produces error JSON fed to LLM; final response used
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Equal("Analysis complete", result.LlmNarrative);
        chatClientMock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}

