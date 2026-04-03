using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.Reporting.Infrastructure;
using OpsCopilot.Reporting.Infrastructure.ServiceBus;
using Xunit;

namespace OpsCopilot.Modules.Reporting.Tests;

public sealed class AzureServiceBusEvidenceProviderTests
{
    private static readonly Guid RunId = Guid.NewGuid();
    private const string TenantId = "tenant-1";

    private static AzureServiceBusEvidenceProvider CreateSut(IQueueInfoSource source) =>
        new(source, NullLogger<AzureServiceBusEvidenceProvider>.Instance);

    private static async IAsyncEnumerable<QueueInfo> FromItems(
        IEnumerable<QueueInfo> items,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
        }
    }

    // AC-134: DLQ > 0 → "critical"
    [Fact]
    public async Task GetSignalsAsync_WhenDlqAboveZero_ReturnsHealthSignalCritical()
    {
        var source = new Mock<IQueueInfoSource>(MockBehavior.Strict);
        source.Setup(s => s.GetQueuesAsync(It.IsAny<CancellationToken>()))
              .Returns((CancellationToken ct) => FromItems([new QueueInfo("q1", 0, 1)], ct));

        var result = await CreateSut(source.Object).GetSignalsAsync(RunId, TenantId, default);

        Assert.NotNull(result);
        Assert.Single(result.Queues);
        Assert.Equal("critical", result.Queues[0].HealthSignal);
    }

    // AC-135: active > 100 (no DLQ) → "warning"
    [Fact]
    public async Task GetSignalsAsync_WhenActiveAboveThresholdAndNoDlq_ReturnsHealthSignalWarning()
    {
        var source = new Mock<IQueueInfoSource>(MockBehavior.Strict);
        source.Setup(s => s.GetQueuesAsync(It.IsAny<CancellationToken>()))
              .Returns((CancellationToken ct) => FromItems([new QueueInfo("q1", 101, 0)], ct));

        var result = await CreateSut(source.Object).GetSignalsAsync(RunId, TenantId, default);

        Assert.NotNull(result);
        Assert.Equal("warning", result.Queues[0].HealthSignal);
    }

    // AC-136: active ≤ 100, DLQ = 0 → "healthy"
    [Fact]
    public async Task GetSignalsAsync_WhenBelowThresholds_ReturnsHealthSignalHealthy()
    {
        var source = new Mock<IQueueInfoSource>(MockBehavior.Strict);
        source.Setup(s => s.GetQueuesAsync(It.IsAny<CancellationToken>()))
              .Returns((CancellationToken ct) => FromItems([new QueueInfo("q1", 50, 0)], ct));

        var result = await CreateSut(source.Object).GetSignalsAsync(RunId, TenantId, default);

        Assert.NotNull(result);
        Assert.Equal("healthy", result.Queues[0].HealthSignal);
    }

    // AC-137: 51 queues → only 50 returned (entity cap)
    [Fact]
    public async Task GetSignalsAsync_WhenMoreThanMaxQueues_CapsAt50()
    {
        var queues = Enumerable.Range(1, 51)
            .Select(i => new QueueInfo($"q{i}", 0, 0));

        var source = new Mock<IQueueInfoSource>(MockBehavior.Strict);
        source.Setup(s => s.GetQueuesAsync(It.IsAny<CancellationToken>()))
              .Returns((CancellationToken ct) => FromItems(queues, ct));

        var result = await CreateSut(source.Object).GetSignalsAsync(RunId, TenantId, default);

        Assert.NotNull(result);
        Assert.Equal(50, result.TotalQueues);
        Assert.Equal(50, result.Queues.Count);
    }

    // AC-138: source throws → GetSignalsAsync returns null, no exception propagation
    [Fact]
    public async Task GetSignalsAsync_WhenSourceThrows_ReturnsNull()
    {
        var source = new Mock<IQueueInfoSource>(MockBehavior.Strict);
        source.Setup(s => s.GetQueuesAsync(It.IsAny<CancellationToken>()))
              .Throws(new InvalidOperationException("Service Bus unavailable"));

        var result = await CreateSut(source.Object).GetSignalsAsync(RunId, TenantId, default);

        Assert.Null(result);
    }

    // AC-139: totals aggregate correctly across multiple queues
    [Fact]
    public async Task GetSignalsAsync_WhenMultipleQueues_AggregatesTotalsCorrectly()
    {
        var queues = new[]
        {
            new QueueInfo("q1", 10, 2),
            new QueueInfo("q2", 20, 3),
        };

        var source = new Mock<IQueueInfoSource>(MockBehavior.Strict);
        source.Setup(s => s.GetQueuesAsync(It.IsAny<CancellationToken>()))
              .Returns((CancellationToken ct) => FromItems(queues, ct));

        var result = await CreateSut(source.Object).GetSignalsAsync(RunId, TenantId, default);

        Assert.NotNull(result);
        Assert.Equal(2, result.TotalQueues);
        Assert.Equal(30, result.TotalActiveMessages);
        Assert.Equal(5, result.TotalDeadLetterMessages);
    }
}
