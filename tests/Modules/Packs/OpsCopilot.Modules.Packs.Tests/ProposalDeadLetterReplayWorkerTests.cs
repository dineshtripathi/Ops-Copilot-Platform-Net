using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.Packs.Application.Abstractions;
using OpsCopilot.Packs.Domain.Entities;
using OpsCopilot.WorkerHost.Workers;
using Xunit;

namespace OpsCopilot.Modules.Packs.Tests;

public sealed class ProposalDeadLetterReplayWorkerTests
{
    private static ProposalDeadLetterReplayWorker CreateWorker(
        IProposalDeadLetterRepository repo,
        IPackSafeActionRecorder recorder)
        => new(repo, recorder, NullLogger<ProposalDeadLetterReplayWorker>.Instance);

    /// <summary>
    /// Creates an entry with <paramref name="priorAttempts"/> calls to MarkReplayStarted()
    /// already applied, so entry.ReplayAttempts == priorAttempts when given to the worker.
    /// </summary>
    private static ProposalDeadLetterEntry MakeEntry(int priorAttempts = 0)
    {
        var entry = new ProposalDeadLetterEntry(
            id: Guid.NewGuid(),
            attemptId: Guid.NewGuid(),
            tenantId: "t1",
            triageRunId: Guid.NewGuid(),
            packName: "azure-vm",
            actionId: "sa-restart",
            actionType: "restart_vm",
            parametersJson: null,
            attemptNumber: 1,
            deadLetteredAt: DateTimeOffset.UtcNow,
            errorMessage: "err");
        for (var i = 0; i < priorAttempts; i++)
            entry.MarkReplayStarted();
        return entry;
    }

    private static PackSafeActionRecordResult SuccessResult()
        => new([], 1, 0, 0, []);

    private static PackSafeActionRecordResult FailResult(string error)
        => new([], 0, 0, 1, [error]);

    [Fact]
    public async Task ProcessPendingEntries_NoPendingEntries_DoesNothing()
    {
        var repo = new Mock<IProposalDeadLetterRepository>();
        var recorder = new Mock<IPackSafeActionRecorder>();
        repo.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await CreateWorker(repo.Object, recorder.Object)
            .ProcessPendingEntriesAsync(CancellationToken.None);

        recorder.Verify(r => r.RecordAsync(
            It.IsAny<PackSafeActionRecordRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPendingEntries_RecordSucceeds_MarksSucceeded()
    {
        var entry = MakeEntry(priorAttempts: 0);
        var repo = new Mock<IProposalDeadLetterRepository>();
        var recorder = new Mock<IPackSafeActionRecorder>();
        repo.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([entry]);
        recorder.Setup(r => r.RecordAsync(
                It.IsAny<PackSafeActionRecordRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult());

        await CreateWorker(repo.Object, recorder.Object)
            .ProcessPendingEntriesAsync(CancellationToken.None);

        repo.Verify(r => r.MarkReplayStartedAsync(entry.Id, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.MarkReplaySucceededAsync(entry.Id, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.MarkReplayFailedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.MarkReplayExhaustedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPendingEntries_RecordFails_BelowMaxAttempts_MarksReplayFailed()
    {
        var entry = MakeEntry(priorAttempts: 0); // 0 + 1 = 1 < 3
        var repo = new Mock<IProposalDeadLetterRepository>();
        var recorder = new Mock<IPackSafeActionRecorder>();
        repo.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([entry]);
        recorder.Setup(r => r.RecordAsync(
                It.IsAny<PackSafeActionRecordRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(FailResult("network timeout"));

        await CreateWorker(repo.Object, recorder.Object)
            .ProcessPendingEntriesAsync(CancellationToken.None);

        repo.Verify(r => r.MarkReplayFailedAsync(entry.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.MarkReplayExhaustedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPendingEntries_RecordFails_AtMaxAttempts_MarksReplayExhausted()
    {
        var entry = MakeEntry(priorAttempts: 2); // 2 + 1 = 3 >= 3
        var repo = new Mock<IProposalDeadLetterRepository>();
        var recorder = new Mock<IPackSafeActionRecorder>();
        repo.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([entry]);
        recorder.Setup(r => r.RecordAsync(
                It.IsAny<PackSafeActionRecordRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(FailResult("quota exceeded"));

        await CreateWorker(repo.Object, recorder.Object)
            .ProcessPendingEntriesAsync(CancellationToken.None);

        repo.Verify(r => r.MarkReplayExhaustedAsync(entry.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.MarkReplayFailedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPendingEntries_RecordThrows_BelowMaxAttempts_MarksReplayFailed()
    {
        var entry = MakeEntry(priorAttempts: 0);
        var repo = new Mock<IProposalDeadLetterRepository>();
        var recorder = new Mock<IPackSafeActionRecorder>();
        repo.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([entry]);
        recorder.Setup(r => r.RecordAsync(
                It.IsAny<PackSafeActionRecordRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient"));

        await CreateWorker(repo.Object, recorder.Object)
            .ProcessPendingEntriesAsync(CancellationToken.None);

        repo.Verify(r => r.MarkReplayFailedAsync(
            entry.Id,
            It.Is<string>(s => s.Contains("transient")),
            It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.MarkReplayExhaustedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPendingEntries_RecordThrows_AtMaxAttempts_MarksReplayExhausted()
    {
        var entry = MakeEntry(priorAttempts: 2); // 2 + 1 = 3 >= 3
        var repo = new Mock<IProposalDeadLetterRepository>();
        var recorder = new Mock<IPackSafeActionRecorder>();
        repo.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([entry]);
        recorder.Setup(r => r.RecordAsync(
                It.IsAny<PackSafeActionRecordRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("downstream down"));

        await CreateWorker(repo.Object, recorder.Object)
            .ProcessPendingEntriesAsync(CancellationToken.None);

        repo.Verify(r => r.MarkReplayExhaustedAsync(
            entry.Id,
            It.Is<string>(s => s.Contains("downstream down")),
            It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.MarkReplayFailedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
