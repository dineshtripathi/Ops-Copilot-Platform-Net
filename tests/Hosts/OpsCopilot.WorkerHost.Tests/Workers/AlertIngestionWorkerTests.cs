using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using OpsCopilot.AlertIngestion.Application.Abstractions;
using OpsCopilot.AlertIngestion.Application.Services;
using OpsCopilot.AlertIngestion.Domain.Models;
using OpsCopilot.WorkerHost.Workers;

namespace OpsCopilot.WorkerHost.Tests.Workers;

public class AlertIngestionWorkerTests
{
    private readonly Mock<IAlertIngestionSource> _source = new();
    private readonly Mock<IAlertNormalizer> _normalizer = new();
    private readonly Mock<IAlertTriageDispatcher> _dispatcher = new();
    private readonly Mock<ILogger<AlertIngestionWorker>> _logger = new();

    private AlertIngestionWorker CreateWorker()
    {
        _normalizer.Setup(n => n.CanHandle(It.IsAny<string>())).Returns(true);
        _normalizer.Setup(n => n.ProviderKey).Returns("test_provider");
        var router = new AlertNormalizerRouter(new[] { _normalizer.Object });
        var config = new ConfigurationBuilder().Build();
        return new AlertIngestionWorker(_source.Object, router, _dispatcher.Object, config, _logger.Object);
    }

    private static NormalizedAlert CreateNormalizedAlert() => new()
    {
        Provider = "test_provider",
        AlertExternalId = "ext-1",
        Title = "Test Alert",
        Severity = "Sev1",
        FiredAtUtc = DateTime.UtcNow,
        ResourceId = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm1",
        SourceType = "metric",
        RawPayload = "{}"
    };

    [Fact]
    public async Task ProcessBatchAsync_EmptySource_DoesNotDispatch()
    {
        _source.Setup(s => s.PollAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IncomingAlertMessage>());

        var worker = CreateWorker();
        await worker.ProcessBatchAsync(CancellationToken.None);

        _dispatcher.Verify(
            d => d.DispatchAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessBatchAsync_ValidMessage_NormalizesAndDispatches()
    {
        var message = new IncomingAlertMessage("msg-1", "tenant-1", "test_provider", """{"key":"value"}""", 1);
        _source.Setup(s => s.PollAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { message });
        _normalizer.Setup(n => n.Normalize("test_provider", It.IsAny<JsonElement>()))
            .Returns(CreateNormalizedAlert());
        _dispatcher.Setup(d => d.DispatchAsync("tenant-1", It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var worker = CreateWorker();
        await worker.ProcessBatchAsync(CancellationToken.None);

        _dispatcher.Verify(
            d => d.DispatchAsync("tenant-1", It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _source.Verify(s => s.AcknowledgeAsync("msg-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessBatchAsync_NormalizationFails_BelowMaxRetries_DoesNotDeadLetter()
    {
        var message = new IncomingAlertMessage("msg-1", "tenant-1", "test_provider", """{"key":"value"}""", 1);
        _source.Setup(s => s.PollAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { message });
        _normalizer.Setup(n => n.Normalize(It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Throws(new InvalidOperationException("normalizer failed"));

        var worker = CreateWorker();
        await worker.ProcessBatchAsync(CancellationToken.None);

        _source.Verify(
            s => s.DeadLetterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessBatchAsync_NormalizationFails_AtMaxRetries_DeadLetters()
    {
        var message = new IncomingAlertMessage(
            "msg-1", "tenant-1", "test_provider", """{"key":"value"}""", AlertIngestionWorker.MaxRetries);
        _source.Setup(s => s.PollAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { message });
        _normalizer.Setup(n => n.Normalize(It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Throws(new InvalidOperationException("normalizer failed"));

        var worker = CreateWorker();
        await worker.ProcessBatchAsync(CancellationToken.None);

        _source.Verify(
            s => s.DeadLetterAsync("msg-1", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
