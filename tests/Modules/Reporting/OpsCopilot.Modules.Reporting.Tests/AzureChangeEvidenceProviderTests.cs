using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.Reporting.Infrastructure;
using OpsCopilot.Reporting.Infrastructure.AzureChange;
using Xunit;

namespace OpsCopilot.Modules.Reporting.Tests;

public sealed class AzureChangeEvidenceProviderTests
{
    private static readonly Guid RunId = Guid.NewGuid();
    private const string TenantId = "tenant-1";

    private static AzureChangeEvidenceProvider CreateSut(IAzureDeploymentSource source) =>
        new(source, NullLogger<AzureChangeEvidenceProvider>.Instance);

    private static async IAsyncEnumerable<DeploymentInfo> FromItems(
        IEnumerable<DeploymentInfo> items,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
        }
    }

    // AC-140: empty source → AzureChangeSynthesis with zero deployments (not null)
    [Fact]
    public async Task GetSynthesisAsync_WhenSourceEmpty_ReturnsSynthesisWithZeroDeployments()
    {
        var source = new Mock<IAzureDeploymentSource>(MockBehavior.Strict);
        source.Setup(s => s.GetDeploymentsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns((string _, CancellationToken ct) => FromItems([], ct));

        var result = await CreateSut(source.Object).GetSynthesisAsync(RunId, TenantId, default);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalDeployments);
        Assert.Empty(result.Deployments);
    }

    // AC-141: 25 deployments from source → only 20 captured (MaxDeployments cap)
    [Fact]
    public async Task GetSynthesisAsync_WhenMoreThanMaxDeployments_CapsAt20()
    {
        var deployments = Enumerable.Range(1, 25)
            .Select(i => new DeploymentInfo($"dep{i}", DateTimeOffset.UtcNow, "Succeeded", $"rg{i}"));

        var source = new Mock<IAzureDeploymentSource>(MockBehavior.Strict);
        source.Setup(s => s.GetDeploymentsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns((string _, CancellationToken ct) => FromItems(deployments, ct));

        var result = await CreateSut(source.Object).GetSynthesisAsync(RunId, TenantId, default);

        Assert.NotNull(result);
        Assert.Equal(20, result.TotalDeployments);
        Assert.Equal(20, result.Deployments.Count);
    }

    // AC-142: source throws → GetSynthesisAsync returns null, no exception propagation
    [Fact]
    public async Task GetSynthesisAsync_WhenSourceThrows_ReturnsNull()
    {
        var source = new Mock<IAzureDeploymentSource>(MockBehavior.Strict);
        source.Setup(s => s.GetDeploymentsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Throws(new InvalidOperationException("ARM unavailable"));

        var result = await CreateSut(source.Object).GetSynthesisAsync(RunId, TenantId, default);

        Assert.Null(result);
    }

    // AC-143: provisioning state passthrough — values flow correctly into synthesis
    [Fact]
    public async Task GetSynthesisAsync_WhenDeploymentPresent_PassesThroughFieldsCorrectly()
    {
        var ts = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var info = new DeploymentInfo("my-deployment", ts, "Succeeded", "my-rg");

        var source = new Mock<IAzureDeploymentSource>(MockBehavior.Strict);
        source.Setup(s => s.GetDeploymentsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns((string _, CancellationToken ct) => FromItems([info], ct));

        var result = await CreateSut(source.Object).GetSynthesisAsync(RunId, TenantId, default);

        Assert.NotNull(result);
        Assert.Single(result.Deployments);
        var signal = result.Deployments[0];
        Assert.Equal("my-deployment", signal.DeploymentName);
        Assert.Equal("Succeeded", signal.ProvisioningState);
        Assert.Equal("my-rg", signal.ResourceGroup);
        Assert.Equal(ts, signal.Timestamp);
    }

    // AC-144: null Timestamp handled — AzureDeploymentSignal with Timestamp = null is accepted
    [Fact]
    public async Task GetSynthesisAsync_WhenTimestampIsNull_ReturnsSignalWithNullTimestamp()
    {
        var info = new DeploymentInfo("dep-no-ts", Timestamp: null, "Running", "rg-x");

        var source = new Mock<IAzureDeploymentSource>(MockBehavior.Strict);
        source.Setup(s => s.GetDeploymentsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns((string _, CancellationToken ct) => FromItems([info], ct));

        var result = await CreateSut(source.Object).GetSynthesisAsync(RunId, TenantId, default);

        Assert.NotNull(result);
        Assert.Single(result.Deployments);
        Assert.Null(result.Deployments[0].Timestamp);
    }
}
