using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Moq;
using OpsCopilot.Rag.Application;
using OpsCopilot.Rag.Domain;
using OpsCopilot.Rag.Infrastructure.Retrieval;
using Xunit;

namespace OpsCopilot.Modules.Rag.Tests;

/// <summary>
/// Tests for VectorRunbookIndexer, NullRagRunbookIndexer and RunbookLoader.ToVectorDocuments.
/// Slice 161 / Slice 162 (embedding versioning).
/// </summary>
public sealed class RunbookIndexerTests
{
    // ── helpers ──────────────────────────────────────────────────────────

    private static VectorRunbookDocument MakeDoc(
        string tenantId          = "tenant-a",
        string runbookId         = "cpu-runbook",
        string title             = "CPU Runbook",
        string content           = "Step 1: check CPU.",
        string embeddingModelId  = "test-model",
        string embeddingVersion  = "1")
        => new()
        {
            Id               = Guid.NewGuid(),
            TenantId         = tenantId,
            RunbookId        = runbookId,
            Title            = title,
            Content          = content,
            Tags             = "cpu, performance",
            EmbeddingModelId = embeddingModelId,
            EmbeddingVersion = embeddingVersion,
        };

    private static Mock<IEmbeddingGenerator<string, Embedding<float>>> EmbedderFor(float value = 0.5f)
    {
        var mock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>(MockBehavior.Strict);
        mock.Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                [new Embedding<float>(new ReadOnlyMemory<float>(new[] { value }))]));
        return mock;
    }

    // ── VectorRunbookIndexer ─────────────────────────────────────────────

    [Fact]
    public async Task IndexAsync_CallsEmbedder_WithTitlePlusContent()
    {
        var doc     = MakeDoc(title: "My Title", content: "My Content");
        var capture = new CapturingRunbookCollection();
        var embedder = new Mock<IEmbeddingGenerator<string, Embedding<float>>>(MockBehavior.Strict);

        string? capturedInput = null;
        embedder.Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>(
                (inputs, _, _) => capturedInput = inputs.First())
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                [new Embedding<float>(new ReadOnlyMemory<float>(new[] { 0.5f }))]));

        var sut = new VectorRunbookIndexer(embedder.Object, capture, "test-model", "1");
        await sut.IndexAsync(doc);

        Assert.Equal("My Title\nMy Content", capturedInput);
    }

    [Fact]
    public async Task IndexAsync_UpsertsDocumentWithEmbedding()
    {
        var doc      = MakeDoc();
        var capture  = new CapturingRunbookCollection();
        var embedder = EmbedderFor(0.7f);

        var sut = new VectorRunbookIndexer(embedder.Object, capture, "test-model", "1");
        await sut.IndexAsync(doc);

        Assert.Single(capture.Upserted);
        var stored = capture.Upserted[0];
        Assert.Equal(doc.TenantId, stored.TenantId);
        Assert.Equal(doc.RunbookId, stored.RunbookId);
        Assert.Equal(doc.Title, stored.Title);
        Assert.Equal(doc.Content, stored.Content);
        Assert.Equal(doc.Tags, stored.Tags);
        Assert.False(stored.Embedding.IsEmpty);
    }

    [Fact]
    public async Task IndexAsync_PreservesId()
    {
        var doc     = MakeDoc();
        var capture = new CapturingRunbookCollection();

        var sut = new VectorRunbookIndexer(EmbedderFor().Object, capture, "test-model", "1");
        await sut.IndexAsync(doc);

        Assert.Equal(doc.Id, capture.Upserted[0].Id);
    }

    [Fact]
    public async Task IndexBatchAsync_IndexesEachDocument()
    {
        var docs    = Enumerable.Range(1, 3).Select(i => MakeDoc(runbookId: $"rb-{i:D3}")).ToList();
        var capture = new CapturingRunbookCollection();

        var sut = new VectorRunbookIndexer(EmbedderFor().Object, capture, "test-model", "1");
        await sut.IndexBatchAsync(docs);

        Assert.Equal(3, capture.Upserted.Count);
    }

    [Fact]
    public async Task IndexAsync_EmptyTitle_EmbedsFallsBackToContent()
    {
        var doc     = MakeDoc(title: "", content: "Only content here.");
        var capture = new CapturingRunbookCollection();

        string? capturedInput = null;
        var embedder = new Mock<IEmbeddingGenerator<string, Embedding<float>>>(MockBehavior.Strict);
        embedder.Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>(
                (inputs, _, _) => capturedInput = inputs.First())
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                [new Embedding<float>(new ReadOnlyMemory<float>(new[] { 0.1f }))]));

        var sut = new VectorRunbookIndexer(embedder.Object, capture, "test-model", "1");
        await sut.IndexAsync(doc);

        Assert.Equal("Only content here.", capturedInput);
    }

    // ── NullRagRunbookIndexer ────────────────────────────────────────────

    [Fact]
    public async Task NullIndexer_IndexAsync_CompletesWithoutException()
    {
        var sut = new NullRagRunbookIndexer();
        await sut.IndexAsync(MakeDoc()); // should not throw
    }

    [Fact]
    public async Task NullIndexer_IndexBatchAsync_CompletesWithoutException()
    {
        var sut  = new NullRagRunbookIndexer();
        var docs = new[] { MakeDoc(), MakeDoc(runbookId: "rb-002") };
        await sut.IndexBatchAsync(docs); // should not throw
    }

    // ── Embedding versioning (Slice 162 / PDD §2.2.10) ───────────────────

    [Fact]
    public async Task IndexAsync_StampsEmbeddingModelIdAndVersion()
    {
        var doc     = MakeDoc();
        var capture = new CapturingRunbookCollection();

        var sut = new VectorRunbookIndexer(EmbedderFor().Object, capture, "text-embedding-3-small", "2");
        await sut.IndexAsync(doc);

        var stored = capture.Upserted[0];
        Assert.Equal("text-embedding-3-small", stored.EmbeddingModelId);
        Assert.Equal("2", stored.EmbeddingVersion);
    }

    // ── RunbookLoader.ToVectorDocuments ──────────────────────────────────

    [Fact]
    public void ToVectorDocuments_NonExistentDirectory_ReturnsEmpty()
    {
        using var logger = new FakeLogger();
        var result = RunbookLoader.ToVectorDocuments(
            @"C:\does-not-exist\path", "tenant-x", logger);

        Assert.Empty(result);
    }

    [Fact]
    public void ToVectorDocuments_SetsCorrectTenantId()
    {
        using var dir    = new TempMarkdownDir("# CPU Spike\ntags: cpu\n\nStep 1.");
        using var logger = new FakeLogger();

        var results = RunbookLoader.ToVectorDocuments(dir.Path, "my-tenant", logger);

        Assert.All(results, d => Assert.Equal("my-tenant", d.TenantId));
    }

    [Fact]
    public void ToVectorDocuments_DeterministicGuid_SameInputProducesSameGuid()
    {
        using var dir    = new TempMarkdownDir("# CPU\ntags: cpu\n\nDetails.");
        using var logger = new FakeLogger();

        var first  = RunbookLoader.ToVectorDocuments(dir.Path, "tenant-a", logger);
        var second = RunbookLoader.ToVectorDocuments(dir.Path, "tenant-a", logger);

        Assert.Equal(first[0].Id, second[0].Id);
    }

    [Fact]
    public void ToVectorDocuments_DifferentTenants_ProduceDifferentGuids()
    {
        using var dir    = new TempMarkdownDir("# Network\ntags: net\n\nContent.");
        using var logger = new FakeLogger();

        var tenantA = RunbookLoader.ToVectorDocuments(dir.Path, "tenant-a", logger);
        var tenantB = RunbookLoader.ToVectorDocuments(dir.Path, "tenant-b", logger);

        Assert.NotEqual(tenantA[0].Id, tenantB[0].Id);
    }

    [Fact]
    public void ToVectorDocuments_TagsJoinedAsCommaList()
    {
        using var dir    = new TempMarkdownDir("# Disk\ntags: disk, io, storage\n\nContent.");
        using var logger = new FakeLogger();

        var results = RunbookLoader.ToVectorDocuments(dir.Path, "t", logger);

        Assert.Contains("disk", results[0].Tags);
        Assert.Contains("io", results[0].Tags);
    }

    // ── Capturing collection (records upserts) ────────────────────────────

    private sealed class CapturingRunbookCollection : VectorStoreCollection<Guid, VectorRunbookDocument>
    {
        public List<VectorRunbookDocument> Upserted { get; } = [];

        public override string Name => "test";

        public override Task<Guid> UpsertAsync(
            VectorRunbookDocument record, CancellationToken cancellationToken = default)
        {
            Upserted.Add(record);
            return Task.FromResult(record.Id);
        }

        public override Task UpsertAsync(
            IEnumerable<VectorRunbookDocument> records, CancellationToken cancellationToken = default)
        {
            foreach (var r in records) Upserted.Add(r);
            return Task.CompletedTask;
        }

        public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public override Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public override Task<VectorRunbookDocument?> GetAsync(
            Guid key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public override IAsyncEnumerable<VectorRunbookDocument> GetAsync(
            Expression<Func<VectorRunbookDocument, bool>> filter, int top,
            FilteredRecordRetrievalOptions<VectorRunbookDocument>? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public override Task DeleteAsync(Guid key, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public override async IAsyncEnumerable<VectorSearchResult<VectorRunbookDocument>> SearchAsync<TInput>(
            TInput vector, int top,
            VectorSearchOptions<VectorRunbookDocument>? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield break;
        }
        public override object? GetService(Type serviceType, object? serviceKey = null) => null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private sealed class FakeLogger : Microsoft.Extensions.Logging.ILogger, IDisposable
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => this;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => false;
        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) { }
        public void Dispose() { }
    }

    private sealed class TempMarkdownDir : IDisposable
    {
        public string Path { get; }

        public TempMarkdownDir(string content)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
            Directory.CreateDirectory(Path);
            File.WriteAllText(System.IO.Path.Combine(Path, "test-runbook.md"), content);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
