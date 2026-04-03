using Microsoft.Agents.Builder;
using Microsoft.Agents.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.AgentRuns.Application.Abstractions;
using Xunit;
using OpsCopilot.AgentRuns.Application.Orchestration;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Models;

namespace OpsCopilot.Modules.AgentRuns.Tests;

/// <summary>
/// Slice 147 / 148: Unit tests for TriageAgentActivityHandler (MAF IAgent adapter).
/// Slice 148 adds ChannelData envelope extraction — tests verify tenantId/workspaceId/fingerprint routing.
/// </summary>
public sealed class TriageAgentActivityHandlerTests
{
    private readonly Mock<ITriageOrchestrator> _orchestrator;
    private readonly TriageAgentActivityHandler _sut;

    public TriageAgentActivityHandlerTests()
    {
        _orchestrator = new Mock<ITriageOrchestrator>(MockBehavior.Strict);
        _sut = new TriageAgentActivityHandler(
            _orchestrator.Object,
            NullLogger<TriageAgentActivityHandler>.Instance);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static TriageResult MakeResult(string? narrative = null) =>
        new(
            RunId:                  Guid.NewGuid(),
            Status:                 AgentRunStatus.Completed,
            SummaryJson:            null,
            Citations:              Array.Empty<KqlCitation>(),
            RunbookCitations:       Array.Empty<RunbookCitation>(),
            MemoryCitations:        Array.Empty<MemoryCitation>(),
            DeploymentDiffCitations: Array.Empty<DeploymentDiffCitation>(),
            SessionId:              null,
            IsNewSession:           false,
            SessionExpiresAtUtc:    null,
            UsedSessionContext:     false,
            SessionReasonCode:      "none",
            LlmNarrative:           narrative);

    private static (Mock<ITurnContext> ctx, Mock<IActivity> activity) MakeContext(
        string activityType,
        string activityId = "act-001",
        object? channelData = null)
    {
        var activity = new Mock<IActivity>(MockBehavior.Loose);
        activity.SetupGet(a => a.Type).Returns(activityType);
        activity.SetupGet(a => a.Id).Returns(activityId);
        if (channelData is not null)
            activity.SetupGet(a => a.ChannelData).Returns(channelData);

        var ctx = new Mock<ITurnContext>(MockBehavior.Loose);
        ctx.SetupGet(c => c.Activity).Returns(activity.Object);

        return (ctx, activity);
    }

    // Returns an anonymous object matching the expected ChannelData JSON shape.
    private static object Envelope(
        string tenantId     = "tenant-abc",
        string workspaceId  = "ws-001",
        string? fingerprint = null) =>
        fingerprint is null
            ? new { tenantId, workspaceId } as object
            : new { tenantId, workspaceId, alertFingerprint = fingerprint };

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnTurnAsync_MessageActivity_DelegatesToOrchestrator_SendsNarrative()
    {
        // Arrange — Slice 148: supply valid ChannelData so envelope parsing succeeds
        var (ctx, _) = MakeContext(ActivityTypes.Message, "act-001",
            Envelope(tenantId: "tenant-abc", workspaceId: "ws-001"));

        string? capturedReply = null;
        ctx.Setup(c => c.SendActivityAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>(
                (text, _, _, _) => capturedReply = text)
            .ReturnsAsync(new ResourceResponse());

        // Slice 148: tenantId and workspaceId come from ChannelData; fingerprint falls back to activityId
        _orchestrator
            .Setup(o => o.RunAsync(
                "tenant-abc", "act-001", "ws-001",
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<RunContext?>(),
                It.IsAny<AgentRun?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult("System is healthy."));

        // Act
        await _sut.OnTurnAsync(ctx.Object, CancellationToken.None);

        // Assert
        Assert.Equal("System is healthy.", capturedReply);
        _orchestrator.VerifyAll();
    }

    [Fact]
    public async Task OnTurnAsync_MessageActivity_NoNarrative_SendsStatusFallback()
    {
        // Arrange — Slice 148: supply valid ChannelData
        var (ctx, _) = MakeContext(ActivityTypes.Message, "act-002",
            Envelope(tenantId: "tenant-abc", workspaceId: "ws-001"));

        string? capturedReply = null;
        ctx.Setup(c => c.SendActivityAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>(
                (text, _, _, _) => capturedReply = text)
            .ReturnsAsync(new ResourceResponse());

        _orchestrator
            .Setup(o => o.RunAsync(
                "tenant-abc", "act-002", "ws-001",
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<RunContext?>(),
                It.IsAny<AgentRun?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult(narrative: null));

        // Act
        await _sut.OnTurnAsync(ctx.Object, CancellationToken.None);

        // Assert — fallback text contains the status name
        Assert.NotNull(capturedReply);
        Assert.Contains(AgentRunStatus.Completed.ToString(), capturedReply);
    }

    [Theory]
    [InlineData(ActivityTypes.Event)]
    [InlineData(ActivityTypes.ConversationUpdate)]
    [InlineData(ActivityTypes.Invoke)]
    public async Task OnTurnAsync_NonMessageActivity_SkipsOrchestratorAndSendActivity(string type)
    {
        // Arrange
        var (ctx, _) = MakeContext(type);

        // Act
        await _sut.OnTurnAsync(ctx.Object, CancellationToken.None);

        // Assert
        _orchestrator.VerifyNoOtherCalls();
        ctx.Verify(c => c.SendActivityAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnTurnAsync_OrchestratorThrows_ExceptionPropagates()
    {
        // Arrange — Slice 148: supply valid ChannelData so envelope parsing succeeds
        var (ctx, _) = MakeContext(ActivityTypes.Message, "act-003",
            Envelope(tenantId: "tenant-abc", workspaceId: "ws-001"));

        _orchestrator
            .Setup(o => o.RunAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<RunContext?>(),
                It.IsAny<AgentRun?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("orchestrator failure"));

        // Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.OnTurnAsync(ctx.Object, CancellationToken.None));
    }

    [Fact]
    public async Task OnTurnAsync_NullActivityId_GeneratesFingerprintAndDoesNotThrow()
    {
        // Arrange — Slice 148: ChannelData with tenantId but no alertFingerprint,
        // so the handler falls back to the generated GUID fingerprint.
        var activity = new Mock<IActivity>(MockBehavior.Loose);
        activity.SetupGet(a => a.Type).Returns(ActivityTypes.Message);
        string? nullId = null;
        activity.SetupGet(a => a.Id).Returns(nullId!); // explicitly null to exercise fallback fingerprint generation
        activity.SetupGet(a => a.ChannelData).Returns(Envelope(tenantId: "tenant-abc", workspaceId: "ws-001"));

        var ctx = new Mock<ITurnContext>(MockBehavior.Loose);
        ctx.SetupGet(c => c.Activity).Returns(activity.Object);
        ctx.Setup(c => c.SendActivityAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResourceResponse());

        _orchestrator
            .Setup(o => o.RunAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<RunContext?>(),
                It.IsAny<AgentRun?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult("OK"));

        // Act — must not throw
        await _sut.OnTurnAsync(ctx.Object, CancellationToken.None);

        // Assert — orchestrator was called with a non-empty generated fingerprint
        _orchestrator.Verify(o => o.RunAsync(
            It.IsAny<string>(),
            It.Is<string>(fp => !string.IsNullOrEmpty(fp)),
            It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<RunContext?>(),
            It.IsAny<AgentRun?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Slice 148: envelope extraction tests ─────────────────────────────────

    [Fact]
    public async Task OnTurnAsync_NullChannelData_RepliesWithErrorAndSkipsOrchestrator()
    {
        // Arrange — no ChannelData set; activity.ChannelData returns null (MockBehavior.Loose default)
        var (ctx, _) = MakeContext(ActivityTypes.Message, "act-e1");

        string? capturedReply = null;
        ctx.Setup(c => c.SendActivityAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>(
                (text, _, _, _) => capturedReply = text)
            .ReturnsAsync(new ResourceResponse());

        // Act
        await _sut.OnTurnAsync(ctx.Object, CancellationToken.None);

        // Assert — error reply sent, orchestrator never called
        _orchestrator.VerifyNoOtherCalls();
        Assert.NotNull(capturedReply);
        Assert.Contains("tenantId", capturedReply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnTurnAsync_MissingTenantIdInChannelData_RepliesWithErrorAndSkipsOrchestrator()
    {
        // Arrange — ChannelData without tenantId
        var (ctx, _) = MakeContext(ActivityTypes.Message, "act-e2",
            new { workspaceId = "ws-001" });

        string? capturedReply = null;
        ctx.Setup(c => c.SendActivityAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>(
                (text, _, _, _) => capturedReply = text)
            .ReturnsAsync(new ResourceResponse());

        // Act
        await _sut.OnTurnAsync(ctx.Object, CancellationToken.None);

        // Assert — error reply sent, orchestrator never called
        _orchestrator.VerifyNoOtherCalls();
        Assert.NotNull(capturedReply);
        Assert.Contains("tenantId", capturedReply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnTurnAsync_ChannelDataWithExplicitFingerprint_UsesEnvelopeFingerprint()
    {
        // Arrange — ChannelData supplies all three fields; fingerprint must override activityId
        const string explicitFingerprint = "sha256-explicit-fp";
        var (ctx, _) = MakeContext(ActivityTypes.Message, "act-004",
            Envelope(tenantId: "tenant-xyz", workspaceId: "ws-xyz", fingerprint: explicitFingerprint));

        ctx.Setup(c => c.SendActivityAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResourceResponse());

        _orchestrator
            .Setup(o => o.RunAsync(
                "tenant-xyz", explicitFingerprint, "ws-xyz",
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<RunContext?>(),
                It.IsAny<AgentRun?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult("OK"));

        // Act
        await _sut.OnTurnAsync(ctx.Object, CancellationToken.None);

        // Assert — orchestrator received the envelope fingerprint, not the activity id
        _orchestrator.VerifyAll();
    }
}
