using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.Rag.Application;
using OpsCopilot.Rag.Application.Memory;
using OpsCopilot.Rag.Domain;
using OpsCopilot.Rag.Infrastructure.Memory;
using OpsCopilot.Rag.Infrastructure.Retrieval;
using Xunit;

namespace OpsCopilot.Modules.Rag.Tests;

/// <summary>
/// Slice 167 — Composite RAG dual-write and composite retrieval.
/// Covers CompositeRunbookIndexer, CompositeRunbookRetrievalService,
/// CompositeIncidentMemoryIndexer, and CompositeIncidentMemoryRetrievalService.
/// </summary>
public sealed class CompositeRagTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static VectorRunbookDocument MakeRunbookDoc(string runbookId = "rb-1")
        => new()
        {
            Id               = Guid.NewGuid(),
            TenantId         = "tenant-x",
            RunbookId        = runbookId,
            Title            = runbookId,
            Content          = "content",
            Tags             = "",
            EmbeddingModelId = "m",
            EmbeddingVersion = "1",
        };

    private static IncidentMemoryDocument MakeMemoryDoc(string runId = "run-1")
        => new()
        {
            Id               = Guid.NewGuid(),
            TenantId         = "tenant-x",
            AlertFingerprint = "fp-1",
            RunId            = runId,
            SummaryText      = "disk full",
            CreatedAtUtc     = DateTimeOffset.UtcNow,
        };

    private static RunbookSearchResult Hit(string runbookId, double score)
        => new(runbookId, runbookId, "snippet", score);

    private static IncidentMemoryHit MemHit(string runId, double score)
        => new(runId, "tenant-x", "fp", "summary", score, DateTimeOffset.UtcNow);

    // ── CompositeRunbookIndexer ───────────────────────────────────────────────

    [Fact]
    public async Task CompositeRunbookIndexer_WritesBothIndexers()
    {
        var primary   = new Mock<IRunbookIndexer>(MockBehavior.Strict);
        var secondary = new Mock<IRunbookIndexer>(MockBehavior.Strict);
        var doc       = MakeRunbookDoc();

        primary.Setup(x => x.IndexAsync(doc, default)).Returns(Task.CompletedTask);
        secondary.Setup(x => x.IndexAsync(doc, default)).Returns(Task.CompletedTask);

        var sut = new CompositeRunbookIndexer(
            primary.Object, secondary.Object,
            NullLogger<CompositeRunbookIndexer>.Instance);

        await sut.IndexAsync(doc);

        primary.Verify(x => x.IndexAsync(doc, default), Times.Once);
        secondary.Verify(x => x.IndexAsync(doc, default), Times.Once);
    }

    [Fact]
    public async Task CompositeRunbookIndexer_ContinuesWhenSecondaryFails()
    {
        var doc       = MakeRunbookDoc();
        var primary   = new Mock<IRunbookIndexer>(MockBehavior.Strict);
        var secondary = new Mock<IRunbookIndexer>(MockBehavior.Strict);

        primary.Setup(x => x.IndexAsync(doc, default)).Returns(Task.CompletedTask);
        secondary.Setup(x => x.IndexAsync(doc, default))
                 .ThrowsAsync(new InvalidOperationException("secondary down"));

        var sut = new CompositeRunbookIndexer(
            primary.Object, secondary.Object,
            NullLogger<CompositeRunbookIndexer>.Instance);

        // Must not throw
        await sut.IndexAsync(doc);

        primary.Verify(x => x.IndexAsync(doc, default), Times.Once);
    }

    // ── CompositeRunbookRetrievalService ──────────────────────────────────────

    [Fact]
    public async Task CompositeRunbookRetrieval_MergesDeduplicatesAndOrdersByScore()
    {
        var query   = new RunbookSearchQuery("disk full", MaxResults: 3);
        var primary = new Mock<IRunbookRetrievalService>(MockBehavior.Strict);
        var secondary = new Mock<IRunbookRetrievalService>(MockBehavior.Strict);

        primary.Setup(x => x.SearchAsync(query, default))
               .ReturnsAsync(new List<RunbookSearchResult> { Hit("rb-A", 0.9), Hit("rb-B", 0.7) });
        secondary.Setup(x => x.SearchAsync(query, default))
                 .ReturnsAsync(new List<RunbookSearchResult> { Hit("rb-B", 0.8), Hit("rb-C", 0.6) });

        var sut = new CompositeRunbookRetrievalService(
            primary.Object, secondary.Object,
            NullLogger<CompositeRunbookRetrievalService>.Instance);

        var results = await sut.SearchAsync(query);

        // rb-B appears in both — deduplicated, primary version kept (score 0.7 not 0.8)
        // Ordered by descending score: rb-A (0.9), rb-B (0.7), rb-C (0.6)
        Assert.Equal(3, results.Count);
        Assert.Equal("rb-A", results[0].RunbookId);
        Assert.Equal("rb-B", results[1].RunbookId);
        Assert.Equal("rb-C", results[2].RunbookId);
    }

    [Fact]
    public async Task CompositeRunbookRetrieval_ReturnsSecondaryResultsWhenPrimaryEmpty()
    {
        var query     = new RunbookSearchQuery("oom", MaxResults: 5);
        var primary   = new Mock<IRunbookRetrievalService>(MockBehavior.Strict);
        var secondary = new Mock<IRunbookRetrievalService>(MockBehavior.Strict);

        primary.Setup(x => x.SearchAsync(query, default))
               .ReturnsAsync(Array.Empty<RunbookSearchResult>());
        secondary.Setup(x => x.SearchAsync(query, default))
                 .ReturnsAsync(new List<RunbookSearchResult> { Hit("rb-X", 0.85) });

        var sut = new CompositeRunbookRetrievalService(
            primary.Object, secondary.Object,
            NullLogger<CompositeRunbookRetrievalService>.Instance);

        var results = await sut.SearchAsync(query);

        Assert.Single(results);
        Assert.Equal("rb-X", results[0].RunbookId);
    }

    // ── CompositeIncidentMemoryIndexer ────────────────────────────────────────

    [Fact]
    public async Task CompositeIncidentMemoryIndexer_WritesBothIndexers()
    {
        var doc       = MakeMemoryDoc();
        var primary   = new Mock<IIncidentMemoryIndexer>(MockBehavior.Strict);
        var secondary = new Mock<IIncidentMemoryIndexer>(MockBehavior.Strict);

        primary.Setup(x => x.IndexAsync(doc, default)).Returns(Task.CompletedTask);
        secondary.Setup(x => x.IndexAsync(doc, default)).Returns(Task.CompletedTask);

        var sut = new CompositeIncidentMemoryIndexer(
            primary.Object, secondary.Object,
            NullLogger<CompositeIncidentMemoryIndexer>.Instance);

        await sut.IndexAsync(doc);

        primary.Verify(x => x.IndexAsync(doc, default), Times.Once);
        secondary.Verify(x => x.IndexAsync(doc, default), Times.Once);
    }

    // ── CompositeIncidentMemoryRetrievalService ───────────────────────────────

    [Fact]
    public async Task CompositeIncidentMemoryRetrieval_DeduplicatesByRunId()
    {
        var query     = new IncidentMemoryQuery("disk full", "tenant-x", MaxResults: 4);
        var primary   = new Mock<IIncidentMemoryRetrievalService>(MockBehavior.Strict);
        var secondary = new Mock<IIncidentMemoryRetrievalService>(MockBehavior.Strict);

        primary.Setup(x => x.SearchAsync(query, default))
               .ReturnsAsync(new List<IncidentMemoryHit> { MemHit("run-1", 0.9), MemHit("run-2", 0.75) });
        secondary.Setup(x => x.SearchAsync(query, default))
                 .ReturnsAsync(new List<IncidentMemoryHit> { MemHit("run-2", 0.8), MemHit("run-3", 0.6) });

        var sut = new CompositeIncidentMemoryRetrievalService(
            primary.Object, secondary.Object,
            NullLogger<CompositeIncidentMemoryRetrievalService>.Instance);

        var results = await sut.SearchAsync(query);

        // run-2 deduplicated; ordered by score: run-1 (0.9), run-2 (0.75), run-3 (0.6)
        Assert.Equal(3, results.Count);
        Assert.Equal("run-1", results[0].RunId);
        Assert.Equal("run-2", results[1].RunId);
        Assert.Equal("run-3", results[2].RunId);
    }
}
