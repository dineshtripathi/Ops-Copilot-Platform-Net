using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Application.Acl;
using OpsCopilot.AgentRuns.Application.Orchestration;
using System.Net.Http;
using System.Runtime.CompilerServices;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

/// <summary>
/// Unit tests for <see cref="ChatOrchestrator.ChatStreamingAsync"/>.
/// Covers happy-path delta streaming, null-client degraded fallback, and client-throws fallback.
/// Slice 185.
/// </summary>
public sealed class ChatOrchestratorStreamingTests
{
    private const string TenantId = "tenant-streaming-001";
    private const string Query    = "What caused the alert?";

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a fake <see cref="IAsyncEnumerable{ChatResponseUpdate}"/> from a list of text deltas.
    /// </summary>
    private static async IAsyncEnumerable<ChatResponseUpdate> FakeStream(
        IEnumerable<string> deltas,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var d in deltas)
        {
            ct.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(ChatRole.Assistant, d);
        }
    }

    /// <summary>
    /// Builds the collapsed service-level mocks shared across most tests.
    /// Memory and runbook return empty results so they never interfere with delta assertions.
    /// </summary>
    private static (Mock<IIncidentMemoryService> memory, Mock<IRunbookSearchToolClient> runbook)
        CreateEmptyServiceMocks()
    {
        var memory = new Mock<IIncidentMemoryService>(MockBehavior.Strict);
        memory
            .Setup(m => m.RecallAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MemoryCitation>());

        var emptyRunbookResponse = new RunbookSearchToolResponse(
            Ok:    true,
            Hits:  Array.Empty<RunbookSearchHit>(),
            Query: Query);

        var runbook = new Mock<IRunbookSearchToolClient>(MockBehavior.Strict);
        runbook
            .Setup(r => r.ExecuteAsync(
                It.IsAny<RunbookSearchToolRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyRunbookResponse);

        return (memory, runbook);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Happy path: streaming deltas arrive in order
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChatStreamingAsync_StreamsDeltas_WhenClientReturnsMultipleUpdates()
    {
        // Arrange
        var (memory, runbook) = CreateEmptyServiceMocks();

        // IChatClient is Loose because we only care about GetStreamingResponseAsync;
        // other interface members (GetService, Dispose) are not called and need no assertion.
        var chatMock = new Mock<IChatClient>(MockBehavior.Loose);
        chatMock
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(FakeStream(["Hello", " world", "!"]));

        var sut = new ChatOrchestrator(
            memory.Object, runbook.Object,
            new PermissiveRunbookAclFilter(),
            NullLogger<ChatOrchestrator>.Instance,
            chatClient: chatMock.Object);

        // Act
        var deltas = new List<string>();
        await foreach (var d in sut.ChatStreamingAsync(TenantId, Query))
            deltas.Add(d);

        // Assert — all three deltas arrive in order
        Assert.Equal(3, deltas.Count);
        Assert.Equal("Hello",  deltas[0]);
        Assert.Equal(" world", deltas[1]);
        Assert.Equal("!",      deltas[2]);

        chatMock.Verify(c => c.GetStreamingResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Null-client path: degraded message emitted as single delta
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChatStreamingAsync_YieldsDegradedMessage_WhenClientIsNull()
    {
        // Arrange
        var (memory, runbook) = CreateEmptyServiceMocks();

        var sut = new ChatOrchestrator(
            memory.Object, runbook.Object,
            new PermissiveRunbookAclFilter(),
            NullLogger<ChatOrchestrator>.Instance,
            chatClient: null);

        // Act
        var deltas = new List<string>();
        await foreach (var d in sut.ChatStreamingAsync(TenantId, Query))
            deltas.Add(d);

        // Assert — exactly one degraded delta
        var single = Assert.Single(deltas);
        Assert.Contains("not available", single, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Client-throws path: error captured and emitted as single delta
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChatStreamingAsync_YieldsErrorMessage_WhenClientThrows()
    {
        // Arrange
        var (memory, runbook) = CreateEmptyServiceMocks();

        var chatMock = new Mock<IChatClient>(MockBehavior.Loose);
        chatMock
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new HttpRequestException("LLM service unavailable"));

        var sut = new ChatOrchestrator(
            memory.Object, runbook.Object,
            new PermissiveRunbookAclFilter(),
            NullLogger<ChatOrchestrator>.Instance,
            chatClient: chatMock.Object);

        // Act
        var deltas = new List<string>();
        await foreach (var d in sut.ChatStreamingAsync(TenantId, Query))
            deltas.Add(d);

        // Assert — exactly one error delta, no exception propagated
        var single = Assert.Single(deltas);
        Assert.Contains("Unable to generate", single, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Empty-text deltas are filtered out
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChatStreamingAsync_FiltersEmptyDeltas_WhenClientReturnsWhitespace()
    {
        // Arrange
        var (memory, runbook) = CreateEmptyServiceMocks();

        var chatMock = new Mock<IChatClient>(MockBehavior.Loose);
        chatMock
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(FakeStream(["", "Hello", null!, ""]));

        var sut = new ChatOrchestrator(
            memory.Object, runbook.Object,
            new PermissiveRunbookAclFilter(),
            NullLogger<ChatOrchestrator>.Instance,
            chatClient: chatMock.Object);

        // Act
        var deltas = new List<string>();
        await foreach (var d in sut.ChatStreamingAsync(TenantId, Query))
            deltas.Add(d);

        // Assert — only the non-empty delta passes through
        var single = Assert.Single(deltas);
        Assert.Equal("Hello", single);
    }
}
