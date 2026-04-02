using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.Rag.Application;
using OpsCopilot.Rag.Domain;
using OpsCopilot.Rag.Infrastructure.Retrieval;
using Xunit;

namespace OpsCopilot.Modules.Rag.Tests;

/// <summary>
/// Tests for RunbookReindexService (Slice 183).
/// </summary>
public sealed class RunbookReindexServiceTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static RunbookReindexService BuildSut(
        IRunbookIndexer indexer,
        string          runbookPath)
        => new(indexer, runbookPath, NullLogger<RunbookReindexService>.Instance);

    // ── contract ─────────────────────────────────────────────────────────────

    [Fact]
    public void ImplementsInterface()
        => Assert.IsAssignableFrom<IRunbookReindexService>(
               BuildSut(new Mock<IRunbookIndexer>().Object, Path.GetTempPath()));

    // ── empty / missing directory ─────────────────────────────────────────────

    [Fact]
    public async Task ReindexAllAsync_EmptyDirectory_ReturnsZero()
    {
        using var dir   = new TempDir();
        var       mock  = new Mock<IRunbookIndexer>(MockBehavior.Strict);
        var       sut   = BuildSut(mock.Object, dir.Path);

        var count = await sut.ReindexAllAsync("tenant-1");

        Assert.Equal(0, count);
        mock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReindexAllAsync_MissingDirectory_ReturnsZero()
    {
        var mock = new Mock<IRunbookIndexer>(MockBehavior.Strict);
        var sut  = BuildSut(mock.Object, "/nonexistent/path/xyz-reindex-test");

        var count = await sut.ReindexAllAsync("tenant-1");

        Assert.Equal(0, count);
        mock.VerifyNoOtherCalls();
    }

    // ── happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReindexAllAsync_SingleMarkdownFile_CallsIndexBatchAsyncOnce()
    {
        using var dir = new TempDir();
        await File.WriteAllTextAsync(
            Path.Combine(dir.Path, "runbook1.md"),
            "# Test Runbook\ntags: ops\nSome runbook content.");

        var captured = new List<VectorRunbookDocument>();
        var mock     = new Mock<IRunbookIndexer>(MockBehavior.Strict);
        mock.Setup(i => i.IndexBatchAsync(
                It.IsAny<IEnumerable<VectorRunbookDocument>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<VectorRunbookDocument>, CancellationToken>(
                (docs, _) => captured.AddRange(docs))
            .Returns(Task.CompletedTask);

        var sut   = BuildSut(mock.Object, dir.Path);
        var count = await sut.ReindexAllAsync("tenant-a");

        Assert.Equal(1, count);
        Assert.Single(captured);
        Assert.Equal("tenant-a", captured[0].TenantId);
        mock.Verify(i => i.IndexBatchAsync(
            It.IsAny<IEnumerable<VectorRunbookDocument>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReindexAllAsync_MultipleMarkdownFiles_ReturnsCorrectCount()
    {
        using var dir = new TempDir();
        await File.WriteAllTextAsync(Path.Combine(dir.Path, "a.md"), "# A\nContent A.");
        await File.WriteAllTextAsync(Path.Combine(dir.Path, "b.md"), "# B\nContent B.");

        var mock = new Mock<IRunbookIndexer>();
        mock.Setup(i => i.IndexBatchAsync(
                It.IsAny<IEnumerable<VectorRunbookDocument>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut   = BuildSut(mock.Object, dir.Path);
        var count = await sut.ReindexAllAsync("tenant-multi");

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task ReindexAllAsync_SetsTenantIdOnAllDocuments()
    {
        using var dir = new TempDir();
        await File.WriteAllTextAsync(Path.Combine(dir.Path, "x.md"), "# X\nContent X.");
        await File.WriteAllTextAsync(Path.Combine(dir.Path, "y.md"), "# Y\nContent Y.");

        var captured = new List<VectorRunbookDocument>();
        var mock     = new Mock<IRunbookIndexer>();
        mock.Setup(i => i.IndexBatchAsync(
                It.IsAny<IEnumerable<VectorRunbookDocument>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<VectorRunbookDocument>, CancellationToken>(
                (docs, _) => captured.AddRange(docs))
            .Returns(Task.CompletedTask);

        var sut = BuildSut(mock.Object, dir.Path);
        await sut.ReindexAllAsync("my-tenant");

        Assert.All(captured, d => Assert.Equal("my-tenant", d.TenantId));
    }

    [Fact]
    public async Task ReindexAllAsync_NonMarkdownFiles_AreIgnored()
    {
        using var dir = new TempDir();
        await File.WriteAllTextAsync(Path.Combine(dir.Path, "notes.txt"), "Some text.");
        await File.WriteAllTextAsync(Path.Combine(dir.Path, "data.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(dir.Path, "runbook.md"), "# R\nContent.");

        var mock = new Mock<IRunbookIndexer>();
        mock.Setup(i => i.IndexBatchAsync(
                It.IsAny<IEnumerable<VectorRunbookDocument>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut   = BuildSut(mock.Object, dir.Path);
        var count = await sut.ReindexAllAsync("t");

        // Only the .md file counts
        Assert.Equal(1, count);
    }

    // ── helper ────────────────────────────────────────────────────────────────

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } =
            Directory.CreateTempSubdirectory("rag-reindex-test-").FullName;

        public void Dispose() =>
            Directory.Delete(Path, recursive: true);
    }
}
