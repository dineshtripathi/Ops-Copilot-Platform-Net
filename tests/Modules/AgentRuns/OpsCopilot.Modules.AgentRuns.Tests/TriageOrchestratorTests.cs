using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Application.Orchestration;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.Governance.Application.Models;
using OpsCopilot.Governance.Application.Policies;
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
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<CancellationToken>()))
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

        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert
        Assert.Equal(agentRun.RunId, result.RunId);
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.NotEmpty(result.Citations);
        Assert.Equal(WorkspaceId, result.Citations[0].WorkspaceId);

        // Verify both repository calls were made
        repoMock.Verify(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()), Times.Once);
        repoMock.Verify(r => r.CompleteRunAsync(
            agentRun.RunId, AgentRunStatus.Completed,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        // Policy events for allowlist + budget (both allowed)
        repoMock.Verify(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
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
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<CancellationToken>()))
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

        var (allowlist, budget, _) = CreateAllowAllGovernanceMocks();
        // HttpRequestException → TOOL_HTTP_ERROR, IsDegraded=true
        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);
        degraded
            .Setup(d => d.MapFailure(It.IsAny<Exception>()))
            .Returns(new DegradedDecision(true, "TOOL_HTTP_ERROR", "Tool connectivity failure", true));

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert: degraded but still has citations (evidence of failure)
        Assert.Equal(agentRun.RunId, result.RunId);
        Assert.Equal(AgentRunStatus.Degraded, result.Status);
        Assert.Null(result.SummaryJson);
        Assert.NotEmpty(result.Citations);

        // The tool call must still be persisted even on failure (audit trail)
        repoMock.Verify(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()), Times.Once);
        repoMock.Verify(r => r.CompleteRunAsync(
            agentRun.RunId, AgentRunStatus.Degraded,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        // allowlist + budget + degraded = 3 policy events
        repoMock.Verify(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
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
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<CancellationToken>()))
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

        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object);

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
        var agentRun  = AgentRun.Create(TenantId, AlertFingerprint);
        var repoMock  = CreateHappyPathRepo(agentRun);
        var kqlMock   = CreateHappyPathKql();
        var logMock   = new Mock<ILogger<TriageOrchestrator>>();
        var (allowlist, budget, degraded) = CreateAllowAllGovernanceMocks();

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, logMock.Object,
            allowlist.Object, budget.Object, degraded.Object);

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
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<CancellationToken>()))
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

        var logMock = new Mock<ILogger<TriageOrchestrator>>();

        var (allowlist, budget, _) = CreateAllowAllGovernanceMocks();
        // InvalidOperationException → UNKNOWN_FAILURE, IsDegraded=true
        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);
        degraded
            .Setup(d => d.MapFailure(It.IsAny<Exception>()))
            .Returns(new DegradedDecision(true, "UNKNOWN_FAILURE", "An unexpected error occurred", false));

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, logMock.Object,
            allowlist.Object, budget.Object, degraded.Object);

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
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<CancellationToken>()))
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

        var allowlist = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        allowlist
            .Setup(a => a.CanUseTool(TenantId, "kql_query"))
            .Returns(PolicyDecision.Deny("TOOL_NOT_ALLOWED", "kql_query is not in the tenant allowlist"));

        var budget = new Mock<ITokenBudgetPolicy>(MockBehavior.Strict);
        // budget should NOT be called — allowlist short-circuits first
        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert
        Assert.Equal(agentRun.RunId, result.RunId);
        Assert.Equal(AgentRunStatus.Failed, result.Status);
        Assert.Empty(result.Citations);

        // MCP was NEVER called
        kqlMock.Verify(k => k.ExecuteAsync(It.IsAny<KqlToolRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        // Only 1 policy event (allowlist deny)
        repoMock.Verify(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_BudgetDenied_ReturnsFailedAndNoMcpCall()
    {
        // Arrange
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        repoMock
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<CancellationToken>()))
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

        var allowlist = new Mock<IToolAllowlistPolicy>(MockBehavior.Strict);
        allowlist
            .Setup(a => a.CanUseTool(TenantId, "kql_query"))
            .Returns(PolicyDecision.Allow());

        var budget = new Mock<ITokenBudgetPolicy>(MockBehavior.Strict);
        budget
            .Setup(b => b.CheckRunBudget(TenantId, It.IsAny<Guid>()))
            .Returns(BudgetDecision.Deny("BUDGET_EXCEEDED", "Tenant monthly token budget exhausted"));

        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert
        Assert.Equal(agentRun.RunId, result.RunId);
        Assert.Equal(AgentRunStatus.Failed, result.Status);
        Assert.Empty(result.Citations);

        // MCP was NEVER called
        kqlMock.Verify(k => k.ExecuteAsync(It.IsAny<KqlToolRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        // 2 policy events: allowlist (allow) + budget (deny)
        repoMock.Verify(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_ToolThrows_DegradedPolicyMapsToFailedStatus()
    {
        // Arrange — degraded policy returns IsDegraded=false → maps to Failed
        var agentRun = AgentRun.Create(TenantId, AlertFingerprint);

        var repoMock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        repoMock
            .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<CancellationToken>()))
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

        var (allowlist, budget, _) = CreateAllowAllGovernanceMocks();
        // UnauthorizedAccessException → AUTH_FAILURE, IsDegraded=false (→ Failed)
        var degraded = new Mock<IDegradedModePolicy>(MockBehavior.Strict);
        degraded
            .Setup(d => d.MapFailure(It.IsAny<Exception>()))
            .Returns(new DegradedDecision(false, "AUTH_FAILURE", "Authentication failed", false));

        var sut = new TriageOrchestrator(
            repoMock.Object, kqlMock.Object, NullLogger<TriageOrchestrator>.Instance,
            allowlist.Object, budget.Object, degraded.Object);

        // Act
        var result = await sut.RunAsync(TenantId, AlertFingerprint, WorkspaceId, Minutes);

        // Assert: IsDegraded=false → maps to Failed, NOT Degraded
        Assert.Equal(agentRun.RunId, result.RunId);
        Assert.Equal(AgentRunStatus.Failed, result.Status);
        Assert.NotEmpty(result.Citations);

        // 3 policy events: allowlist + budget + degraded
        repoMock.Verify(r => r.AppendPolicyEventAsync(It.IsAny<AgentRunPolicyEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        repoMock.Verify(r => r.CompleteRunAsync(
            agentRun.RunId, AgentRunStatus.Failed,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers — shared mock setup
    // ─────────────────────────────────────────────────────────────────────────

    private static Mock<IAgentRunRepository> CreateHappyPathRepo(AgentRun agentRun)
    {
        var mock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        mock.Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<CancellationToken>()))
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
}
