using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Application.Orchestration;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Repositories;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

/// <summary>
/// Unit tests for <see cref="TriageOrchestrator"/>.
/// 
/// Scope: orchestration logic only — no IO.
/// Dependencies are stubbed via Moq.
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

        var sut = new TriageOrchestrator(repoMock.Object, kqlMock.Object, NullLogger<TriageOrchestrator>.Instance);

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

        var sut = new TriageOrchestrator(repoMock.Object, kqlMock.Object, NullLogger<TriageOrchestrator>.Instance);

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

        var sut = new TriageOrchestrator(repoMock.Object, kqlMock.Object, NullLogger<TriageOrchestrator>.Instance);

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

        var sut = new TriageOrchestrator(repoMock.Object, kqlMock.Object, logMock.Object);

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
            .Setup(r => r.CompleteRunAsync(
                agentRun.RunId, AgentRunStatus.Degraded,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var kqlMock = new Mock<IKqlToolClient>(MockBehavior.Strict);
        kqlMock
            .Setup(k => k.ExecuteAsync(It.IsAny<KqlToolRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("KQL timeout"));

        var logMock = new Mock<ILogger<TriageOrchestrator>>();

        var sut = new TriageOrchestrator(repoMock.Object, kqlMock.Object, logMock.Object);

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
    // Helpers — shared mock setup for logging tests
    // ─────────────────────────────────────────────────────────────────────────

    private static Mock<IAgentRunRepository> CreateHappyPathRepo(AgentRun agentRun)
    {
        var mock = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        mock.Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentRun);
        mock.Setup(r => r.AppendToolCallAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()))
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
}
