using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.Packs.Application.Abstractions;
using OpsCopilot.Packs.Domain.Entities;
using OpsCopilot.WorkerHost.Workers;

namespace OpsCopilot.WorkerHost.Tests.Workers;

public class ProposalDeadLetterReplayWorkerTests
{
    private readonly Mock<IProposalDeadLetterRepository> _repository = new();
    private readonly Mock<IPackSafeActionRecorder> _recorder = new();
    private readonly Mock<ILogger<ProposalDeadLetterReplayWorker>> _logger = new();

    private ProposalDeadLetterReplayWorker CreateWorker() =>
        new(_repository.Object, _recorder.Object, _logger.Object);

    private static ProposalDeadLetterEntry CreateEntry(int priorReplayAttempts = 0)
    {
        var entry = new ProposalDeadLetterEntry(
            id: Guid.NewGuid(),
            attemptId: Guid.NewGuid(),
            tenantId: "tenant-1",
            triageRunId: Guid.NewGuid(),
            packName: "core-diagnostics",
            actionId: "restart-service",
            actionType: "diagnose",
            parametersJson: """{"key":"value"}""",
            attemptNumber: 1,
            deadLetteredAt: DateTimeOffset.UtcNow,
            errorMessage: "original error");

        // Simulate prior replay cycles to reach the desired ReplayAttempts value.
        for (var i = 0; i < priorReplayAttempts; i++)
        {
            entry.MarkReplayStarted();
            entry.MarkReplayFailed($"prior failure {i + 1}");
        }

        return entry;
    }

    private static PackSafeActionRecordResult SuccessResult() =>
        new(Records: Array.Empty<PackSafeActionRecordItem>(),
            CreatedCount: 1, SkippedCount: 0, FailedCount: 0,
            Errors: Array.Empty<string>());

    private static PackSafeActionRecordResult FailureResult(string error = "Record failed") =>
        new(Records: Array.Empty<PackSafeActionRecordItem>(),
            CreatedCount: 0, SkippedCount: 0, FailedCount: 1,
            Errors: new[] { error });

    [Fact]
    public async Task ProcessPendingEntriesAsync_NoPendingEntries_DoesNotCallRecorder()
    {
        _repository.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ProposalDeadLetterEntry>());

        var worker = CreateWorker();
        await worker.ProcessPendingEntriesAsync(CancellationToken.None);

        _recorder.Verify(
            r => r.RecordAsync(It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessPendingEntriesAsync_SuccessfulReplay_MarksSucceeded()
    {
        var entry = CreateEntry();
        _repository.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { entry });
        _recorder.Setup(r => r.RecordAsync(It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult());

        var worker = CreateWorker();
        await worker.ProcessPendingEntriesAsync(CancellationToken.None);

        _repository.Verify(r => r.MarkReplayStartedAsync(entry.Id, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.MarkReplaySucceededAsync(entry.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessPendingEntriesAsync_FailedReplay_UnderMax_MarksReplayFailed()
    {
        var entry = CreateEntry(priorReplayAttempts: 0);
        _repository.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { entry });
        _recorder.Setup(r => r.RecordAsync(It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FailureResult());

        var worker = CreateWorker();
        await worker.ProcessPendingEntriesAsync(CancellationToken.None);

        _repository.Verify(
            r => r.MarkReplayFailedAsync(entry.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(
            r => r.MarkReplayExhaustedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPendingEntriesAsync_FailedReplay_AtMax_MarksExhausted()
    {
        var entry = CreateEntry(priorReplayAttempts: 2);
        _repository.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { entry });
        _recorder.Setup(r => r.RecordAsync(It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FailureResult());

        var worker = CreateWorker();
        await worker.ProcessPendingEntriesAsync(CancellationToken.None);

        _repository.Verify(
            r => r.MarkReplayExhaustedAsync(entry.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(
            r => r.MarkReplayFailedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPendingEntriesAsync_RecorderThrows_UnderMax_MarksReplayFailed()
    {
        var entry = CreateEntry(priorReplayAttempts: 0);
        _repository.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { entry });
        _recorder.Setup(r => r.RecordAsync(It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("recorder error"));

        var worker = CreateWorker();
        await worker.ProcessPendingEntriesAsync(CancellationToken.None);

        _repository.Verify(
            r => r.MarkReplayFailedAsync(entry.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
