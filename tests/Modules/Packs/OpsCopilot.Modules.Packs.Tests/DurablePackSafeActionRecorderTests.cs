using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.Packs.Application.Abstractions;
using OpsCopilot.Packs.Domain.Models;
using OpsCopilot.Packs.Infrastructure;
using Xunit;

namespace OpsCopilot.Modules.Packs.Tests;

/// <summary>
/// Unit tests for <see cref="DurablePackSafeActionRecorder"/> — the retry + dead-letter
/// decorator around <see cref="IPackSafeActionRecorder"/>.
/// </summary>
public sealed class DurablePackSafeActionRecorderTests
{
    private const string TestTenantId = "tenant-durable-test";
    private static readonly Guid TestRunId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    // ── Helpers ────────────────────────────────────────────────

    private static PackSafeActionRecordItem CreateItem(
        string packName = "azure-vm",
        string actionId = "sa-restart",
        string actionType = "restart_vm",
        string status = "Created",
        string? errorMessage = null,
        string? policyDenialReasonCode = null) =>
        new(packName, actionId, actionType, Guid.NewGuid(), status, errorMessage, policyDenialReasonCode);

    private static PackSafeActionProposalItem CreateProposal(
        string packName = "azure-vm",
        string actionId = "sa-restart",
        string actionType = "restart_vm",
        string? parametersJson = """{"size":"standard"}""") =>
        new(
            PackName: packName,
            ActionId: actionId,
            DisplayName: $"Test {actionId}",
            ActionType: actionType,
            RequiresMode: "C",
            DefinitionFile: $"actions/{actionId}.json",
            ParametersJson: parametersJson,
            ErrorMessage: null,
            IsExecutableNow: true,
            ExecutionBlockedReason: null);

    private static PackSafeActionRecordRequest MakeRequest(
        string tenantId = TestTenantId,
        Guid? runId = null,
        IReadOnlyList<PackSafeActionProposalItem>? proposals = null) =>
        new(
            DeploymentMode: "C",
            TenantId: tenantId,
            TriageRunId: runId ?? TestRunId,
            Proposals: proposals ?? Array.Empty<PackSafeActionProposalItem>());

    private static PackSafeActionRecordResult CreateResult(
        IReadOnlyList<PackSafeActionRecordItem> items) =>
        new(
            Records: items,
            CreatedCount: items.Count(i => i.Status == "Created"),
            SkippedCount: items.Count(i => i.Status == "Skipped"),
            FailedCount: items.Count(i => i.Status == "Failed"),
            Errors: items.Where(i => i.ErrorMessage is not null)
                         .Select(i => i.ErrorMessage!)
                         .ToList());

    /// <summary>Loose policy mock: max N attempts, all delays zero.</summary>
    private static Mock<IProposalRecordingRetryPolicy> BuildPolicyMock(int maxAttempts = 3)
    {
        var m = new Mock<IProposalRecordingRetryPolicy>(MockBehavior.Loose);
        m.SetupGet(p => p.MaxAttempts).Returns(maxAttempts);
        m.Setup(p => p.ShouldRetry(It.Is<int>(n => n <= maxAttempts))).Returns(true);
        m.Setup(p => p.ShouldRetry(It.Is<int>(n => n > maxAttempts))).Returns(false);
        m.Setup(p => p.GetDelay(It.IsAny<int>())).Returns(TimeSpan.Zero);
        return m;
    }

    private static DurablePackSafeActionRecorder CreateSut(
        IPackSafeActionRecorder inner,
        IProposalRecordingRetryPolicy? policy = null,
        IProposalDeadLetterStore? deadLetter = null) =>
        new(
            inner,
            policy ?? BuildPolicyMock().Object,
            deadLetter ?? new Mock<IProposalDeadLetterStore>(MockBehavior.Loose).Object,
            NullLogger<DurablePackSafeActionRecorder>.Instance);

    // ── Tests ──────────────────────────────────────────────────

    /// <summary>When inner returns zero failures the result is passed through unchanged (fast path).</summary>
    [Fact]
    public async Task RecordAsync_NoFailures_ReturnsFastPath()
    {
        var inner = new Mock<IPackSafeActionRecorder>(MockBehavior.Strict);
        var expected = CreateResult([CreateItem(status: "Created")]);
        inner.Setup(r => r.RecordAsync(It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(expected);

        var deadLetter = new Mock<IProposalDeadLetterStore>(MockBehavior.Strict);
        var sut = CreateSut(inner.Object, deadLetter: deadLetter.Object);

        var actual = await sut.RecordAsync(MakeRequest(), CancellationToken.None);

        Assert.Same(expected, actual);
        inner.Verify(r => r.RecordAsync(It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        deadLetter.VerifyNoOtherCalls();
    }

    /// <summary>Failed item retried once; success on second attempt — no dead-letter.</summary>
    [Fact]
    public async Task RecordAsync_FailedItem_RetriedOnce_Succeeds()
    {
        var proposal = CreateProposal(actionId: "sa-resize");
        var request = MakeRequest(proposals: [proposal]);

        var inner = new Mock<IPackSafeActionRecorder>(MockBehavior.Strict);
        inner.SetupSequence(r => r.RecordAsync(It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(CreateResult([CreateItem(actionId: "sa-resize", status: "Failed", errorMessage: "timeout")]))
             .ReturnsAsync(CreateResult([CreateItem(actionId: "sa-resize", status: "Created")]));

        var deadLetter = new Mock<IProposalDeadLetterStore>(MockBehavior.Strict);

        // maxAttempts=2 → ShouldRetry(2)=true, ShouldRetry(3)=false — allows exactly one retry
        var sut = CreateSut(inner.Object, BuildPolicyMock(maxAttempts: 2).Object, deadLetter.Object);

        var actual = await sut.RecordAsync(request, CancellationToken.None);

        Assert.Equal(1, actual.CreatedCount);
        Assert.Equal(0, actual.FailedCount);
        inner.Verify(r => r.RecordAsync(It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        deadLetter.VerifyNoOtherCalls();
    }

    /// <summary>All retries exhausted → remaining failed item is dead-lettered.</summary>
    [Fact]
    public async Task RecordAsync_AllRetriesExhausted_ItemDeadLettered()
    {
        const string errorMsg = "db-unavailable";
        const string paramsJson = """{"vm":"vm-01"}""";
        var proposal = CreateProposal(actionId: "sa-restart", parametersJson: paramsJson);
        var request = MakeRequest(proposals: [proposal]);

        var inner = new Mock<IPackSafeActionRecorder>(MockBehavior.Loose);
        inner.Setup(r => r.RecordAsync(It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(CreateResult([CreateItem(actionId: "sa-restart", status: "Failed", errorMessage: errorMsg)]));

        var deadLetter = new Mock<IProposalDeadLetterStore>(MockBehavior.Loose);
        deadLetter.Setup(d => d.AddAsync(It.IsAny<ProposalRecordingAttempt>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        // maxAttempts=3 → ShouldRetry(2,3)=true, ShouldRetry(4)=false → inner called 3 times
        var sut = CreateSut(inner.Object, BuildPolicyMock(maxAttempts: 3).Object, deadLetter.Object);

        var actual = await sut.RecordAsync(request, CancellationToken.None);

        Assert.Equal(1, actual.FailedCount);
        inner.Verify(
            r => r.RecordAsync(It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        deadLetter.Verify(
            d => d.AddAsync(
                It.Is<ProposalRecordingAttempt>(a =>
                    a.ActionId == "sa-restart" &&
                    a.TenantId == TestTenantId &&
                    a.TriageRunId == TestRunId &&
                    a.IsDeadLettered &&
                    a.ErrorMessage == errorMsg &&
                    a.ParametersJson == paramsJson),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>PolicyDenied items have FailedCount=0 from inner → fast-path — never retried.</summary>
    [Fact]
    public async Task RecordAsync_PolicyDeniedItem_FastPath_InnerCalledOnce()
    {
        var inner = new Mock<IPackSafeActionRecorder>(MockBehavior.Strict);
        var expected = CreateResult([CreateItem(status: "PolicyDenied", policyDenialReasonCode: "denied")]);
        inner.Setup(r => r.RecordAsync(It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(expected);

        var deadLetter = new Mock<IProposalDeadLetterStore>(MockBehavior.Strict);
        var sut = CreateSut(inner.Object, deadLetter: deadLetter.Object);

        var actual = await sut.RecordAsync(MakeRequest(), CancellationToken.None);

        Assert.Same(expected, actual);
        inner.Verify(r => r.RecordAsync(It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        deadLetter.VerifyNoOtherCalls();
    }

    /// <summary>Skipped items have FailedCount=0 from inner → fast-path — never retried.</summary>
    [Fact]
    public async Task RecordAsync_SkippedItem_FastPath_InnerCalledOnce()
    {
        var inner = new Mock<IPackSafeActionRecorder>(MockBehavior.Strict);
        var expected = CreateResult([CreateItem(status: "Skipped")]);
        inner.Setup(r => r.RecordAsync(It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(expected);

        var deadLetter = new Mock<IProposalDeadLetterStore>(MockBehavior.Strict);
        var sut = CreateSut(inner.Object, deadLetter: deadLetter.Object);

        var actual = await sut.RecordAsync(MakeRequest(), CancellationToken.None);

        Assert.Same(expected, actual);
        inner.Verify(r => r.RecordAsync(It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        deadLetter.VerifyNoOtherCalls();
    }

    /// <summary>GetDelay is called with the current attempt number on each retry iteration.</summary>
    [Fact]
    public async Task RecordAsync_GetDelay_CalledWithAttemptNumber_OnEachRetry()
    {
        var proposal = CreateProposal(actionId: "sa-reboot");
        var request = MakeRequest(proposals: [proposal]);

        var inner = new Mock<IPackSafeActionRecorder>(MockBehavior.Loose);
        inner.SetupSequence(r => r.RecordAsync(It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(CreateResult([CreateItem(actionId: "sa-reboot", status: "Failed", errorMessage: "err1")]))
             .ReturnsAsync(CreateResult([CreateItem(actionId: "sa-reboot", status: "Failed", errorMessage: "err2")]))
             .ReturnsAsync(CreateResult([CreateItem(actionId: "sa-reboot", status: "Created")]));

        var policy = new Mock<IProposalRecordingRetryPolicy>(MockBehavior.Loose);
        policy.Setup(p => p.ShouldRetry(It.Is<int>(n => n <= 3))).Returns(true);
        policy.Setup(p => p.ShouldRetry(It.Is<int>(n => n > 3))).Returns(false);
        policy.Setup(p => p.GetDelay(It.IsAny<int>())).Returns(TimeSpan.Zero);

        var sut = CreateSut(inner.Object, policy.Object);

        await sut.RecordAsync(request, CancellationToken.None);

        policy.Verify(p => p.GetDelay(2), Times.Once);
        policy.Verify(p => p.GetDelay(3), Times.Once);
    }

    /// <summary>
    /// Mixed result: one Created and one Failed item. Only the Failed one is retried
    /// and (when retries exhausted) dead-lettered. The Created item is preserved.
    /// </summary>
    [Fact]
    public async Task RecordAsync_MixedResult_OnlyFailedItemRetried()
    {
        var proposals = new[]
        {
            CreateProposal(actionId: "sa-restart"),
            CreateProposal(actionId: "sa-resize")
        };
        var request = MakeRequest(proposals: proposals);

        var inner = new Mock<IPackSafeActionRecorder>(MockBehavior.Loose);
        // First call: one Created, one Failed
        inner.SetupSequence(r => r.RecordAsync(It.IsAny<PackSafeActionRecordRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(CreateResult([
                 CreateItem(actionId: "sa-restart", status: "Created"),
                 CreateItem(actionId: "sa-resize",  status: "Failed", errorMessage: "timeout")]))
             // Retry sub-request only contains sa-resize → still fails
             .ReturnsAsync(CreateResult([CreateItem(actionId: "sa-resize", status: "Failed", errorMessage: "timeout")]));

        var deadLetter = new Mock<IProposalDeadLetterStore>(MockBehavior.Loose);
        deadLetter.Setup(d => d.AddAsync(It.IsAny<ProposalRecordingAttempt>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        // maxAttempts=2 → one retry, then exhausted
        var sut = CreateSut(inner.Object, BuildPolicyMock(maxAttempts: 2).Object, deadLetter.Object);

        var actual = await sut.RecordAsync(request, CancellationToken.None);

        // Created item preserved; Failed item dead-lettered
        Assert.Equal(1, actual.CreatedCount);
        Assert.Equal(1, actual.FailedCount);
        Assert.Equal(2, actual.Records.Count);

        // Only the Failed item triggers a dead-letter
        deadLetter.Verify(
            d => d.AddAsync(
                It.Is<ProposalRecordingAttempt>(a => a.ActionId == "sa-resize"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
