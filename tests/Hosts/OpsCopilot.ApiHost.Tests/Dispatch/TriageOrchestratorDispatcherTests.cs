using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Application.Orchestration;
using Xunit;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.ApiHost.Dispatch;

namespace OpsCopilot.ApiHost.Tests.Dispatch;

/// <summary>
/// Slice 129: Unit tests for the fire-and-forget failsafe in TriageOrchestratorDispatcher.
/// Verifies that an unhandled exception from ResumeRunAsync drives the AgentRun to
/// a terminal Failed state via CompleteRunAsync, so runs never get stuck in Running.
/// </summary>
public sealed class TriageOrchestratorDispatcherTests
{
    private const string TenantId = "tenant-tests";
    private static readonly Guid RunId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly string Fingerprint = "fp-test-001";

    private static readonly AgentRun SampleRun =
        AgentRun.Create(TenantId, Fingerprint);

    private static readonly TriageResult SuccessResult = new(
        SampleRun.RunId,
        AgentRunStatus.Completed,
        "{}",
        Array.Empty<KqlCitation>(),
        Array.Empty<RunbookCitation>(),
        Array.Empty<MemoryCitation>(),
        Array.Empty<DeploymentDiffCitation>(),
        SessionId: null,
        IsNewSession: false,
        SessionExpiresAtUtc: null,
        UsedSessionContext: false,
        SessionReasonCode: "none");

    /// <summary>
    /// Builds a dispatcher with two separate scoped service providers:
    /// one for the run-lookup phase and one for the fire-and-forget triage phase.
    /// </summary>
    private static (
        TriageOrchestratorDispatcher Dispatcher,
        Mock<ITriageOrchestrator> OrchestratorMock,
        Mock<IAgentRunRepository> TriageRepoMock)
    BuildDispatcher(bool orchestratorThrows)
    {
        // -- Lookup scope (first CreateScope call in DispatchAsync) -----------
        var lookupRepo = new Mock<IAgentRunRepository>();
        lookupRepo
            .Setup(r => r.GetByRunIdAsync(
                It.IsAny<Guid>(), TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleRun);

        var lookupProvider = new Mock<IServiceProvider>();
        lookupProvider
            .Setup(p => p.GetService(typeof(IAgentRunRepository)))
            .Returns(lookupRepo.Object);

        var lookupScope = new Mock<IServiceScope>();
        lookupScope.Setup(s => s.ServiceProvider).Returns(lookupProvider.Object);

        // -- Triage scope (second CreateScope call — inside Task.Run) ---------
        var orchestratorMock = new Mock<ITriageOrchestrator>();
        if (orchestratorThrows)
        {
            orchestratorMock
                .Setup(o => o.ResumeRunAsync(
                    It.IsAny<AgentRun>(), It.IsAny<string>(),
                    It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("simulated pipeline failure"));
        }
        else
        {
            orchestratorMock
                .Setup(o => o.ResumeRunAsync(
                    It.IsAny<AgentRun>(), It.IsAny<string>(),
                    It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(SuccessResult);
        }

        var triageRepoMock = new Mock<IAgentRunRepository>();
        triageRepoMock
            .Setup(r => r.CompleteRunAsync(
                It.IsAny<Guid>(), It.IsAny<AgentRunStatus>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var triageProvider = new Mock<IServiceProvider>();
        triageProvider
            .Setup(p => p.GetService(typeof(ITriageOrchestrator)))
            .Returns(orchestratorMock.Object);
        triageProvider
            .Setup(p => p.GetService(typeof(IAgentRunRepository)))
            .Returns(triageRepoMock.Object);

        var triageScope = new Mock<IServiceScope>();
        triageScope.Setup(s => s.ServiceProvider).Returns(triageProvider.Object);

        // Scope factory returns: lookup scope on call 1, triage scope on call 2+
        var callCount = 0;
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory
            .Setup(f => f.CreateScope())
            .Returns(() => callCount++ == 0 ? lookupScope.Object : triageScope.Object);

        var log = new Mock<ILogger<TriageOrchestratorDispatcher>>();
        var dispatcher = new TriageOrchestratorDispatcher(scopeFactory.Object, log.Object);

        return (dispatcher, orchestratorMock, triageRepoMock);
    }

    [Fact]
    public async Task DispatchAsync_CallsCompleteRunFailed_WhenResumeRunAsyncThrows()
    {
        // Arrange
        var (dispatcher, _, triageRepoMock) = BuildDispatcher(orchestratorThrows: true);

        // Act
        var dispatched = await dispatcher.DispatchAsync(TenantId, RunId, Fingerprint);

        // Await the fire-and-forget task so assertions are deterministic.
        await dispatcher.LastTriageTask!;

        // Assert
        Assert.True(dispatched);
        triageRepoMock.Verify(
            r => r.CompleteRunAsync(
                RunId,
                AgentRunStatus.Failed,
                It.IsAny<string>(),
                "[]",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_DoesNotCallCompleteRunAsync_WhenResumeRunAsyncSucceeds()
    {
        // Arrange
        var (dispatcher, _, triageRepoMock) = BuildDispatcher(orchestratorThrows: false);

        // Act
        var dispatched = await dispatcher.DispatchAsync(TenantId, RunId, Fingerprint);

        // Await the fire-and-forget task so assertions are deterministic.
        await dispatcher.LastTriageTask!;

        // Assert
        Assert.True(dispatched);
        triageRepoMock.Verify(
            r => r.CompleteRunAsync(
                It.IsAny<Guid>(), It.IsAny<AgentRunStatus>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
