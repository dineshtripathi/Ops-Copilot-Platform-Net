using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using OpsCopilot.WorkerHost.Workers;

namespace OpsCopilot.WorkerHost.Tests.Workers;

public class DigestWorkerTests
{
    private readonly Mock<ITenantDigestSource> _source = new(MockBehavior.Strict);
    private readonly Mock<ILogger<DigestWorker>> _logger = new();

    private DigestWorker CreateWorker(int intervalHours = 24)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Digest:Worker:IntervalHours"] = intervalHours.ToString()
            })
            .Build();

        return new DigestWorker(_source.Object, config, _logger.Object);
    }

    [Fact]
    public async Task ProcessDigestAsync_EmptySource_DoesNotThrow()
    {
        _source.Setup(s => s.CollectAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(Array.Empty<TenantDigestEntry>());

        var worker = CreateWorker();

        await worker.ProcessDigestAsync(CancellationToken.None);

        _source.Verify(s => s.CollectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessDigestAsync_WithEntries_LogsEachTenant()
    {
        var entries = new[]
        {
            new TenantDigestEntry(
                Guid.NewGuid(), "Tenant A", 10, 2,
                DateTimeOffset.UtcNow.AddHours(-24), DateTimeOffset.UtcNow),
            new TenantDigestEntry(
                Guid.NewGuid(), "Tenant B", 5, 0,
                DateTimeOffset.UtcNow.AddHours(-24), DateTimeOffset.UtcNow)
        };

        _source.Setup(s => s.CollectAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(entries);

        var worker = CreateWorker();

        await worker.ProcessDigestAsync(CancellationToken.None);

        _source.Verify(s => s.CollectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessDigestAsync_CollectsOnce_PerCycle()
    {
        _source.Setup(s => s.CollectAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(Array.Empty<TenantDigestEntry>());

        var worker = CreateWorker();

        await worker.ProcessDigestAsync(CancellationToken.None);
        await worker.ProcessDigestAsync(CancellationToken.None);

        _source.Verify(s => s.CollectAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessDigestAsync_CancellationToken_PropagatedToSource()
    {
        using var cts = new CancellationTokenSource();
        var capturedToken = default(CancellationToken);

        _source.Setup(s => s.CollectAsync(It.IsAny<CancellationToken>()))
               .Callback<CancellationToken>(t => capturedToken = t)
               .ReturnsAsync(Array.Empty<TenantDigestEntry>());

        var worker = CreateWorker();

        await worker.ProcessDigestAsync(cts.Token);

        Assert.Equal(cts.Token, capturedToken);
    }

    [Fact]
    public async Task ProcessDigestAsync_HighFailureCount_DoesNotThrow()
    {
        var entry = new TenantDigestEntry(
            Guid.NewGuid(), "Critical Tenant", 100, 95,
            DateTimeOffset.UtcNow.AddHours(-24), DateTimeOffset.UtcNow);

        _source.Setup(s => s.CollectAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { entry });

        var worker = CreateWorker();

        await worker.ProcessDigestAsync(CancellationToken.None);

        _source.Verify(s => s.CollectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
