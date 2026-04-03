using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.Reporting.Infrastructure;
using Xunit;

namespace OpsCopilot.Modules.Reporting.Tests;

public sealed class ObservabilityEvidenceProviderTests
{
    [Fact]
    public async Task GetSummaryAsync_WhenWorkspaceMissing_StillExecutesPack()
    {
        var executor = new Mock<IPackEvidenceExecutor>(MockBehavior.Strict);
        executor.Setup(x => x.ExecuteAsync(
                It.Is<PackEvidenceExecutionRequest>(r => r.DeploymentMode == "B" && r.TenantId == "tenant-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackEvidenceExecutionResult([], []));
        var sut = new ObservabilityEvidenceProvider(executor.Object, new ConfigurationBuilder().Build(), NullLogger<ObservabilityEvidenceProvider>.Instance);

        var result = await sut.GetSummaryAsync(Guid.NewGuid(), "tenant-1", null, CancellationToken.None);

        Assert.Null(result);
        executor.Verify(x => x.ExecuteAsync(
            It.Is<PackEvidenceExecutionRequest>(r => r.DeploymentMode == "B" && r.TenantId == "tenant-1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSummaryAsync_WhenAppInsightsPackReturnsCollectors_MapsSafeHighlights()
    {
        var executor = new Mock<IPackEvidenceExecutor>(MockBehavior.Strict);
        executor.Setup(x => x.ExecuteAsync(
                It.Is<PackEvidenceExecutionRequest>(r => r.DeploymentMode == "B" && r.TenantId == "tenant-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackEvidenceExecutionResult(
                [
                    new PackEvidenceItem(
                        PackName: "app-insights",
                        CollectorId: "top-exceptions",
                        ConnectorName: "azure-monitor",
                        QueryFile: "queries/top-exceptions.kql",
                        QueryContent: null,
                        ResultJson: "[{\"exceptionType\":\"NullReferenceException\",\"Count\":42,\"SampleMessage\":\"Object reference not set\"}]",
                        RowCount: 1,
                        ErrorMessage: null),
                    new PackEvidenceItem(
                        PackName: "app-insights",
                        CollectorId: "failed-requests",
                        ConnectorName: "azure-monitor",
                        QueryFile: "queries/failed-requests.kql",
                        QueryContent: null,
                        ResultJson: null,
                        RowCount: 0,
                        ErrorMessage: "workspace query timeout")
                ],
                []));

        var sut = new ObservabilityEvidenceProvider(executor.Object, new ConfigurationBuilder().Build(), NullLogger<ObservabilityEvidenceProvider>.Instance);

        var result = await sut.GetSummaryAsync(Guid.NewGuid(), "tenant-1", "ws-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("app-insights", result!.Source);
        Assert.Equal(2, result.CollectorCount);
        Assert.Equal(1, result.SuccessfulCollectors);
        Assert.Equal(1, result.FailedCollectors);
        Assert.Contains(result.CollectorSummaries, c => c.Title == "Top Exceptions" && c.Highlights[0].Contains("NullReferenceException", StringComparison.Ordinal));
        Assert.Contains(result.CollectorSummaries, c => c.Title == "Failed Requests" && c.ErrorMessage == "workspace query timeout");
    }

    [Fact]
    public async Task GetLiveSummaryAsync_WhenAppInsightsPackReturnsCollectors_MapsSafeHighlights()
    {
        var executor = new Mock<IPackEvidenceExecutor>(MockBehavior.Strict);
        executor.Setup(x => x.ExecuteAsync(
                It.Is<PackEvidenceExecutionRequest>(r => r.DeploymentMode == "B" && r.TenantId == "tenant-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackEvidenceExecutionResult(
                [
                    new PackEvidenceItem(
                        PackName: "app-insights",
                        CollectorId: "error-trends",
                        ConnectorName: "azure-monitor",
                        QueryFile: "queries/error-trends.kql",
                        QueryContent: null,
                        ResultJson: "[{\"ErrorRate\":12.3,\"FailedCount\":7}]",
                        RowCount: 1,
                        ErrorMessage: null)
                ],
                []));

        var sut = new ObservabilityEvidenceProvider(executor.Object, new ConfigurationBuilder().Build(), NullLogger<ObservabilityEvidenceProvider>.Instance);

        var result = await sut.GetLiveSummaryAsync("tenant-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("app-insights", result!.Source);
        Assert.Single(result.CollectorSummaries);
        Assert.Contains("Error rate", result.CollectorSummaries[0].Highlights[0], StringComparison.Ordinal);
        Assert.Equal("live-signal-detected", result.CoverageStatus);
        Assert.True(result.IsActionable);
        Assert.NotNull(result.Recommendations);
        Assert.NotEmpty(result.Recommendations!);
    }

    [Fact]
    public async Task GetLiveSummaryAsync_WhenNoCollectorsReturned_ProvidesCoverageDiagnostic()
    {
        var executor = new Mock<IPackEvidenceExecutor>(MockBehavior.Strict);
        executor.Setup(x => x.ExecuteAsync(
                It.Is<PackEvidenceExecutionRequest>(r => r.DeploymentMode == "B" && r.TenantId == "tenant-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackEvidenceExecutionResult([], []));

        var sut = new ObservabilityEvidenceProvider(executor.Object, new ConfigurationBuilder().Build(), NullLogger<ObservabilityEvidenceProvider>.Instance);

        var result = await sut.GetLiveSummaryAsync("tenant-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("collectors-missing", result!.CoverageStatus);
        Assert.False(result.IsActionable);
        Assert.Contains("not returned", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Recommendations);
        Assert.NotEmpty(result.Recommendations!);
    }

    [Fact]
    public async Task GetLiveImpactSummaryAsync_WhenImpactCollectorsPresent_MapsBlastAndPolicySignals()
    {
        var executor = new Mock<IPackEvidenceExecutor>(MockBehavior.Strict);
        executor.Setup(x => x.ExecuteAsync(
                It.Is<PackEvidenceExecutionRequest>(r => r.DeploymentMode == "B" && r.TenantId == "tenant-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackEvidenceExecutionResult(
                [
                    new PackEvidenceItem(
                        PackName: "app-insights",
                        CollectorId: "live-blast-radius",
                        ConnectorName: "azure-monitor",
                        QueryFile: "queries/live-blast-radius.kql",
                        QueryContent: null,
                        ResultJson: "[{\"impactedSubscriptions\":1,\"impactedResourceGroups\":2,\"impactedResources\":4,\"impactedApplications\":1}]",
                        RowCount: 1,
                        ErrorMessage: null),
                    new PackEvidenceItem(
                        PackName: "app-insights",
                        CollectorId: "live-policy-activity",
                        ConnectorName: "azure-monitor",
                        QueryFile: "queries/live-policy-activity.kql",
                        QueryContent: null,
                        ResultJson: "[{\"TotalPolicyEvents\":9,\"PolicyDenials\":3,\"ScopeDenials\":1,\"BudgetDenials\":0,\"DegradedModeEvents\":2}]",
                        RowCount: 1,
                        ErrorMessage: null)
                ],
                []));

        var sut = new ObservabilityEvidenceProvider(executor.Object, new ConfigurationBuilder().Build(), NullLogger<ObservabilityEvidenceProvider>.Instance);

        var result = await sut.GetLiveImpactSummaryAsync("tenant-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("app-insights", result!.Source);
        Assert.Equal(4, result.BlastRadius!.ImpactedResources);
        Assert.Equal(3, result.ActivitySignals!.PolicyDenials);
        Assert.True(result.IsActionable);
        Assert.Equal("live-impact-detected", result.CoverageStatus);
        Assert.True(result.SuccessfulCollectors >= 2);
        Assert.NotNull(result.Recommendations);
        Assert.NotEmpty(result.Recommendations!);
    }

    [Fact]
    public async Task GetLiveImpactSummaryAsync_WhenCollectorsReturnNoRows_ReturnsDiagnostic()
    {
        var executor = new Mock<IPackEvidenceExecutor>(MockBehavior.Strict);
        executor.Setup(x => x.ExecuteAsync(
                It.Is<PackEvidenceExecutionRequest>(r => r.DeploymentMode == "B" && r.TenantId == "tenant-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackEvidenceExecutionResult(
                [
                    new PackEvidenceItem(
                        PackName: "app-insights",
                        CollectorId: "live-blast-radius",
                        ConnectorName: "azure-monitor",
                        QueryFile: "queries/live-blast-radius.kql",
                        QueryContent: null,
                        ResultJson: "[]",
                        RowCount: 0,
                        ErrorMessage: null),
                    new PackEvidenceItem(
                        PackName: "app-insights",
                        CollectorId: "live-policy-activity",
                        ConnectorName: "azure-monitor",
                        QueryFile: "queries/live-policy-activity.kql",
                        QueryContent: null,
                        ResultJson: "[]",
                        RowCount: 0,
                        ErrorMessage: null)
                ],
                []));

        var sut = new ObservabilityEvidenceProvider(executor.Object, new ConfigurationBuilder().Build(), NullLogger<ObservabilityEvidenceProvider>.Instance);

        var result = await sut.GetLiveImpactSummaryAsync("tenant-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result!.BlastRadius);
        Assert.Null(result.ActivitySignals);
        Assert.Contains("returned zero rows", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.IsActionable);
        Assert.Equal("live-data-no-impact", result.CoverageStatus);
        Assert.NotNull(result.Recommendations);
        Assert.NotEmpty(result.Recommendations!);
    }

    [Fact]
    public async Task GetSummaryAsync_WhenExceptionMessageExceeds200Chars_TruncatesAt200()
    {
        var longMessage = new string('A', 210);
        var executor = new Mock<IPackEvidenceExecutor>(MockBehavior.Strict);
        executor.Setup(x => x.ExecuteAsync(
                It.Is<PackEvidenceExecutionRequest>(r => r.TenantId == "tenant-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackEvidenceExecutionResult(
                [
                    new PackEvidenceItem(
                        PackName: "app-insights",
                        CollectorId: "top-exceptions",
                        ConnectorName: "azure-monitor",
                        QueryFile: "queries/top-exceptions.kql",
                        QueryContent: null,
                        ResultJson: $"[{{\"exceptionType\":\"RuntimeException\",\"Count\":1,\"SampleMessage\":\"{longMessage}\"}}]",
                        RowCount: 1,
                        ErrorMessage: null)
                ],
                []));
        var sut = new ObservabilityEvidenceProvider(executor.Object, new ConfigurationBuilder().Build(), NullLogger<ObservabilityEvidenceProvider>.Instance);

        var result = await sut.GetSummaryAsync(Guid.NewGuid(), "tenant-1", "ws-1", CancellationToken.None);

        Assert.NotNull(result);
        var collector = Assert.Single(result!.CollectorSummaries, c => c.CollectorId == "top-exceptions");
        var highlight = Assert.Single(collector.Highlights);
        Assert.EndsWith("...", highlight, StringComparison.Ordinal);
        Assert.True(highlight.Length <= 250, $"Highlight too long: {highlight.Length}");
        Assert.False(highlight.Contains(longMessage, StringComparison.Ordinal), "Should not contain full un-truncated message");
    }

    [Fact]
    public async Task GetSummaryAsync_WhenResultCodeIsZero_ShowsNoResponse()
    {
        var executor = new Mock<IPackEvidenceExecutor>(MockBehavior.Strict);
        executor.Setup(x => x.ExecuteAsync(
                It.Is<PackEvidenceExecutionRequest>(r => r.TenantId == "tenant-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackEvidenceExecutionResult(
                [
                    new PackEvidenceItem(
                        PackName: "app-insights",
                        CollectorId: "failed-requests",
                        ConnectorName: "azure-monitor",
                        QueryFile: "queries/failed-requests.kql",
                        QueryContent: null,
                        ResultJson: "[{\"resultCode\":\"0\",\"Count\":5,\"url\":\"https://api.example.com/health\"}]",
                        RowCount: 1,
                        ErrorMessage: null)
                ],
                []));
        var sut = new ObservabilityEvidenceProvider(executor.Object, new ConfigurationBuilder().Build(), NullLogger<ObservabilityEvidenceProvider>.Instance);

        var result = await sut.GetSummaryAsync(Guid.NewGuid(), "tenant-1", "ws-1", CancellationToken.None);

        Assert.NotNull(result);
        var collector = Assert.Single(result!.CollectorSummaries, c => c.CollectorId == "failed-requests");
        var highlight = Assert.Single(collector.Highlights);
        Assert.Contains("No response", highlight, StringComparison.Ordinal);
        Assert.DoesNotContain("0 (", highlight, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSummaryAsync_WhenRequestsWith403_DerivesAuthRejectionPattern()
    {
        var executor = new Mock<IPackEvidenceExecutor>(MockBehavior.Strict);
        executor.Setup(x => x.ExecuteAsync(
                It.Is<PackEvidenceExecutionRequest>(r => r.TenantId == "tenant-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackEvidenceExecutionResult(
                [
                    new PackEvidenceItem(
                        PackName: "app-insights",
                        CollectorId: "failed-requests",
                        ConnectorName: "azure-monitor",
                        QueryFile: "queries/failed-requests.kql",
                        QueryContent: null,
                        ResultJson: "[{\"resultCode\":\"403\",\"Count\":12,\"url\":\"https://api.example.com/admin\"}]",
                        RowCount: 1,
                        ErrorMessage: null)
                ],
                []));
        var sut = new ObservabilityEvidenceProvider(executor.Object, new ConfigurationBuilder().Build(), NullLogger<ObservabilityEvidenceProvider>.Instance);

        var result = await sut.GetSummaryAsync(Guid.NewGuid(), "tenant-1", "ws-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result!.FailurePattern);
        Assert.Contains("Auth rejection", result.FailurePattern, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSummaryAsync_WhenFailedDependencyTargetsExternalHost_DerivesExternalVendorOwnerPath()
    {
        var executor = new Mock<IPackEvidenceExecutor>(MockBehavior.Strict);
        executor.Setup(x => x.ExecuteAsync(
                It.Is<PackEvidenceExecutionRequest>(r => r.TenantId == "tenant-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackEvidenceExecutionResult(
                [
                    new PackEvidenceItem(
                        PackName: "app-insights",
                        CollectorId: "failed-dependencies",
                        ConnectorName: "azure-monitor",
                        QueryFile: "queries/failed-dependencies.kql",
                        QueryContent: null,
                        ResultJson: "[{\"target\":\"HTTP: api.beaconcrm.org\",\"Count\":50,\"resultCode\":\"500\"}]",
                        RowCount: 1,
                        ErrorMessage: null)
                ],
                []));
        var sut = new ObservabilityEvidenceProvider(executor.Object, new ConfigurationBuilder().Build(), NullLogger<ObservabilityEvidenceProvider>.Instance);

        var result = await sut.GetSummaryAsync(Guid.NewGuid(), "tenant-1", "ws-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result!.OwnerPath);
        Assert.Contains("External vendor", result.OwnerPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSummaryAsync_WhenTopExceptionsCollector_HasRunbookRef()
    {
        var executor = new Mock<IPackEvidenceExecutor>(MockBehavior.Strict);
        executor.Setup(x => x.ExecuteAsync(
                It.Is<PackEvidenceExecutionRequest>(r => r.TenantId == "tenant-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackEvidenceExecutionResult(
                [
                    new PackEvidenceItem(
                        PackName: "app-insights",
                        CollectorId: "top-exceptions",
                        ConnectorName: "azure-monitor",
                        QueryFile: "queries/top-exceptions.kql",
                        QueryContent: null,
                        ResultJson: "[{\"exceptionType\":\"NullReferenceException\",\"Count\":5}]",
                        RowCount: 1,
                        ErrorMessage: null)
                ],
                []));
        var sut = new ObservabilityEvidenceProvider(executor.Object, new ConfigurationBuilder().Build(), NullLogger<ObservabilityEvidenceProvider>.Instance);

        var result = await sut.GetSummaryAsync(Guid.NewGuid(), "tenant-1", "ws-1", CancellationToken.None);

        Assert.NotNull(result);
        var collector = Assert.Single(result!.CollectorSummaries, c => c.CollectorId == "top-exceptions");
        Assert.NotNull(collector.RunbookRef);
        Assert.Contains("exception-diagnosis", collector.RunbookRef, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSummaryAsync_WhenAvailabilitySignalsCollector_HasNullRunbookRef()
    {
        var executor = new Mock<IPackEvidenceExecutor>(MockBehavior.Strict);
        executor.Setup(x => x.ExecuteAsync(
                It.Is<PackEvidenceExecutionRequest>(r => r.TenantId == "tenant-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackEvidenceExecutionResult(
                [
                    new PackEvidenceItem(
                        PackName: "app-insights",
                        CollectorId: "availability-signals",
                        ConnectorName: "azure-monitor",
                        QueryFile: "queries/availability-signals.kql",
                        QueryContent: null,
                        ResultJson: "[{\"availabilityPercentage\":99.9,\"testName\":\"PingTest\"}]",
                        RowCount: 1,
                        ErrorMessage: null)
                ],
                []));
        var sut = new ObservabilityEvidenceProvider(executor.Object, new ConfigurationBuilder().Build(), NullLogger<ObservabilityEvidenceProvider>.Instance);

        var result = await sut.GetSummaryAsync(Guid.NewGuid(), "tenant-1", "ws-1", CancellationToken.None);

        Assert.NotNull(result);
        var collector = Assert.Single(result!.CollectorSummaries, c => c.CollectorId == "availability-signals");
        Assert.Null(collector.RunbookRef);
    }
}