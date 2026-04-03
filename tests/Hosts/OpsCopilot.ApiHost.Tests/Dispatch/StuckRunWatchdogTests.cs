using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.ApiHost.Dispatch;
using Xunit;

namespace OpsCopilot.ApiHost.Tests.Dispatch;

/// <summary>
/// Slice 130: Unit tests for <see cref="StuckRunWatchdog.ScanAsync"/>.
/// Verifies that stuck runs (Running + too old) are driven to Failed,
/// and that empty results produce no CompleteRunAsync calls.
/// </summary>
public sealed class StuckRunWatchdogTests
{
    private const string TenantId = "tenant-watchdog-tests";
    private const string Fingerprint = "fp-watchdog-001";

    private static readonly AgentRun SampleRun = AgentRun.Create(TenantId, Fingerprint);

    /// <summary>
    /// Builds a <see cref="StuckRunWatchdog"/> with a scoped mock repository.
    /// Returns both the watchdog and the mocked repo for assertion.
    /// </summary>
    private static (StuckRunWatchdog Watchdog, Mock<IAgentRunRepository> RepoMock)
        BuildWatchdog(IReadOnlyList<AgentRun> stuckRuns)
    {
        var repoMock = new Mock<IAgentRunRepository>();
        repoMock
            .Setup(r => r.GetStuckRunsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stuckRuns);

        var scopedProvider = new Mock<IServiceProvider>();
        scopedProvider
            .Setup(p => p.GetService(typeof(IAgentRunRepository)))
            .Returns(repoMock.Object);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(scopedProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory
            .Setup(f => f.CreateScope())
            .Returns(scope.Object);

        var options = Options.Create(new StuckRunWatchdogOptions
        {
            IntervalSeconds = 300,
            ThresholdMinutes = 30
        });

        var logger = new Mock<ILogger<StuckRunWatchdog>>();
        var watchdog = new StuckRunWatchdog(scopeFactory.Object, options, logger.Object);

        return (watchdog, repoMock);
    }

    [Fact]
    public async Task ScanAsync_MarksStuckRunAsFailed_WhenRunningRunExceedsThreshold()
    {
        var (watchdog, repoMock) = BuildWatchdog([SampleRun]);

        await watchdog.ScanAsync();

        repoMock.Verify(
            r => r.CompleteRunAsync(
                SampleRun.RunId,
                AgentRunStatus.Failed,
                It.Is<string>(s => s.Contains("StuckRunWatchdog")),
                "[]",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ScanAsync_DoesNotCallCompleteRunAsync_WhenNoStuckRunsFound()
    {
        var (watchdog, repoMock) = BuildWatchdog([]);

        await watchdog.ScanAsync();

        repoMock.Verify(
            r => r.CompleteRunAsync(
                It.IsAny<Guid>(),
                It.IsAny<AgentRunStatus>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ScanAsync_ContinuesWithOtherRuns_WhenOneCompleteRunAsyncThrows()
    {
        var run1 = AgentRun.Create(TenantId, "fp-001");
        var run2 = AgentRun.Create(TenantId, "fp-002");

        var repoMock = new Mock<IAgentRunRepository>();
        repoMock
            .Setup(r => r.GetStuckRunsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([run1, run2]);

        // First call throws, second should still be called
        repoMock
            .Setup(r => r.CompleteRunAsync(
                run1.RunId, AgentRunStatus.Failed, It.IsAny<string>(), "[]", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        repoMock
            .Setup(r => r.CompleteRunAsync(
                run2.RunId, AgentRunStatus.Failed, It.IsAny<string>(), "[]", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var scopedProvider = new Mock<IServiceProvider>();
        scopedProvider
            .Setup(p => p.GetService(typeof(IAgentRunRepository)))
            .Returns(repoMock.Object);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(scopedProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var options = Options.Create(new StuckRunWatchdogOptions());
        var logger = new Mock<ILogger<StuckRunWatchdog>>();
        var watchdog = new StuckRunWatchdog(scopeFactory.Object, options, logger.Object);

        // Should not throw despite run1's exception
        await watchdog.ScanAsync();

        // run2 must still be marked as Failed
        repoMock.Verify(
            r => r.CompleteRunAsync(
                run2.RunId, AgentRunStatus.Failed, It.IsAny<string>(), "[]", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
