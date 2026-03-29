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
/// Slice 147: Unit tests for TriageAgentActivityHandler (MAF IAgent adapter).
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

    private static (Mock<ITurnContext> ctx, Mock<IActivity> activity) MakeContext(string activityType, string activityId = "act-001")
    {
        var activity = new Mock<IActivity>(MockBehavior.Loose);
        activity.SetupGet(a => a.Type).Returns(activityType);
        activity.SetupGet(a => a.Id).Returns(activityId);

        var ctx = new Mock<ITurnContext>(MockBehavior.Loose);
        ctx.SetupGet(c => c.Activity).Returns(activity.Object);

        return (ctx, activity);
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnTurnAsync_MessageActivity_DelegatesToOrchestrator_SendsNarrative()
    {
        // Arrange
        var (ctx, _) = MakeContext(ActivityTypes.Message, "act-001");

        string? capturedReply = null;
        ctx.Setup(c => c.SendActivityAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>(
                (text, _, _, _) => capturedReply = text)
            .ReturnsAsync(new ResourceResponse());

        _orchestrator
            .Setup(o => o.RunAsync(
                It.IsAny<string>(), "act-001", "default",
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
        // Arrange
        var (ctx, _) = MakeContext(ActivityTypes.Message, "act-002");

        string? capturedReply = null;
        ctx.Setup(c => c.SendActivityAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>(
                (text, _, _, _) => capturedReply = text)
            .ReturnsAsync(new ResourceResponse());

        _orchestrator
            .Setup(o => o.RunAsync(
                It.IsAny<string>(), "act-002", "default",
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
        // Arrange
        var (ctx, _) = MakeContext(ActivityTypes.Message, "act-003");

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
        // Arrange
        var activity = new Mock<IActivity>(MockBehavior.Loose);
        activity.SetupGet(a => a.Type).Returns(ActivityTypes.Message);
        string? nullId = null;
        activity.SetupGet(a => a.Id).Returns(nullId!); // explicitly null to exercise fallback fingerprint generation

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
}
