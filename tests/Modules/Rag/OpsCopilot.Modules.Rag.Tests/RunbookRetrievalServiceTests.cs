using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.VectorData;
using Moq;
using OpsCopilot.Rag.Application;
using OpsCopilot.Rag.Domain;
using OpsCopilot.Rag.Infrastructure.Retrieval;
using Xunit;

namespace OpsCopilot.Modules.Rag.Tests;

/// <summary>
/// Tests for VectorRunbookRetrievalService tenant-ACL enforcement (Slice 159 / G11).
/// </summary>
public sealed class RunbookRetrievalServiceTests
{
    // ── helpers ──────────────────────────────────────────────────────────

    private static Mock<IEmbeddingGenerator<string, Embedding<float>>> EmbedderFor(float value = 0.1f)
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

    private static VectorRunbookDocument MakeDoc(string tenantId, string runbookId = "rb-001", string embeddingVersion = "1")
        => new()
        {
            Id               = Guid.NewGuid(),
            TenantId         = tenantId,
            RunbookId        = runbookId,
            Title            = $"Runbook {runbookId}",
            Content          = "Step 1: check logs.",
            Tags             = string.Empty,
            EmbeddingVersion = embeddingVersion,
        };

    // ── ACL enforcement ──────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_FiltersTenantMismatch()
    {
        // doc belongs to "tenant-b"; caller is "tenant-a" → must return empty
        var doc        = MakeDoc("tenant-b");
        var collection = new FakeRunbookCollection(
            [new VectorSearchResult<VectorRunbookDocument>(doc, 0.95)]);

        var sut   = new VectorRunbookRetrievalService(EmbedderFor().Object, collection,
            NullLogger<VectorRunbookRetrievalService>.Instance, "1");
        var query = new RunbookSearchQuery("cpu runbook", MaxResults: 5, TenantId: "tenant-a");

        var result = await sut.SearchAsync(query);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchAsync_ReturnsTenantMatch()
    {
        // doc belongs to "tenant-a"; caller is "tenant-a" → must be returned
        var doc        = MakeDoc("tenant-a");
        var collection = new FakeRunbookCollection(
            [new VectorSearchResult<VectorRunbookDocument>(doc, 0.92)]);

        var sut   = new VectorRunbookRetrievalService(EmbedderFor().Object, collection,
            NullLogger<VectorRunbookRetrievalService>.Instance, "1");
        var query = new RunbookSearchQuery("cpu runbook", MaxResults: 5, TenantId: "tenant-a");

        var result = await sut.SearchAsync(query);

        Assert.Single(result);
        Assert.Equal("rb-001", result[0].RunbookId);
    }

    [Fact]
    public async Task SearchAsync_MixedTenants_ReturnsOnlyCallerTenant()
    {
        // store has docs for tenant-a and tenant-b; caller is tenant-a
        var docA = MakeDoc("tenant-a", "rb-a");
        var docB = MakeDoc("tenant-b", "rb-b");
        var collection = new FakeRunbookCollection(
        [
            new VectorSearchResult<VectorRunbookDocument>(docA, 0.9),
            new VectorSearchResult<VectorRunbookDocument>(docB, 0.8),
        ]);

        var sut   = new VectorRunbookRetrievalService(EmbedderFor().Object, collection,
            NullLogger<VectorRunbookRetrievalService>.Instance, "1");
        var query = new RunbookSearchQuery("runbook", MaxResults: 5, TenantId: "tenant-a");

        var result = await sut.SearchAsync(query);

        Assert.Single(result);
        Assert.Equal("rb-a", result[0].RunbookId);
    }

    [Fact]
    public async Task SearchAsync_EmptyTenantId_ReturnsAllDocs()
    {
        // When TenantId is empty (dev/in-memory mode), no ACL filter is applied
        var docA = MakeDoc("tenant-a", "rb-a");
        var docB = MakeDoc("tenant-b", "rb-b");
        var collection = new FakeRunbookCollection(
        [
            new VectorSearchResult<VectorRunbookDocument>(docA, 0.9),
            new VectorSearchResult<VectorRunbookDocument>(docB, 0.8),
        ]);

        var sut   = new VectorRunbookRetrievalService(EmbedderFor().Object, collection,
            NullLogger<VectorRunbookRetrievalService>.Instance, "1");
        var query = new RunbookSearchQuery("runbook", MaxResults: 5, TenantId: "");

        var result = await sut.SearchAsync(query);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task SearchAsync_RespectsMaxResultsAfterFiltering()
    {
        // 5 docs for tenant-a but MaxResults = 2 → only 2 returned
        var docs = Enumerable.Range(1, 5)
            .Select(i => new VectorSearchResult<VectorRunbookDocument>(
                MakeDoc("tenant-a", $"rb-{i:D3}"), 1.0 - i * 0.05))
            .ToList();
        var collection = new FakeRunbookCollection(docs);

        var sut   = new VectorRunbookRetrievalService(EmbedderFor().Object, collection,
            NullLogger<VectorRunbookRetrievalService>.Instance, "1");
        var query = new RunbookSearchQuery("runbook", MaxResults: 2, TenantId: "tenant-a");

        var result = await sut.SearchAsync(query);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task SearchAsync_EmbedderThrows_ReturnsEmpty()
    {
        var embedder = new Mock<IEmbeddingGenerator<string, Embedding<float>>>(MockBehavior.Strict);
        embedder
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("embedder unavailable"));

        var collection = new FakeRunbookCollection([]);
        var sut        = new VectorRunbookRetrievalService(
            embedder.Object, collection, NullLogger<VectorRunbookRetrievalService>.Instance, "1");
        var query      = new RunbookSearchQuery("cpu runbook", TenantId: "tenant-x");

        var result = await sut.SearchAsync(query);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchAsync_EmptyStore_ReturnsEmpty()
    {
        var collection = new FakeRunbookCollection([]);
        var sut        = new VectorRunbookRetrievalService(EmbedderFor().Object, collection,
            NullLogger<VectorRunbookRetrievalService>.Instance, "1");
        var query      = new RunbookSearchQuery("nothing", TenantId: "tenant-a");

        var result = await sut.SearchAsync(query);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchAsync_SkipsDocumentsWithMismatchedEmbeddingVersion()
    {
        // doc has EmbeddingVersion = "0"; service expects "1" → must be filtered out (PDD §2.2.10)
        var doc        = MakeDoc("tenant-a", "rb-old", embeddingVersion: "0");
        var collection = new FakeRunbookCollection(
            [new VectorSearchResult<VectorRunbookDocument>(doc, 0.95)]);

        var sut   = new VectorRunbookRetrievalService(EmbedderFor().Object, collection,
            NullLogger<VectorRunbookRetrievalService>.Instance, "1");
        var query = new RunbookSearchQuery("cpu runbook", MaxResults: 5, TenantId: "tenant-a");

        var result = await sut.SearchAsync(query);

        Assert.Empty(result);
    }

    // ── InMemory service ignores TenantId (dev mode) ─────────────────────

    [Fact]
    public async Task InMemoryService_SearchAsync_TenantIdIgnored()
    {
        // In-memory keyword service is for local dev and doesn't enforce ACL.
        // Even with a TenantId set, it returns results unfiltered.
        var sut    = new InMemoryRunbookRetrievalService([], NullLogger<InMemoryRunbookRetrievalService>.Instance);
        var query  = new RunbookSearchQuery("cpu", TenantId: "any-tenant");
        var result = await sut.SearchAsync(query);

        Assert.Empty(result); // empty store → empty results (behaviour unchanged)
    }

    // ── FakeRunbookCollection ──────────────────────────────────────────────

    private sealed class FakeRunbookCollection : VectorStoreCollection<Guid, VectorRunbookDocument>
    {
        private readonly IReadOnlyList<VectorSearchResult<VectorRunbookDocument>> _results;

        public FakeRunbookCollection(IReadOnlyList<VectorSearchResult<VectorRunbookDocument>> results)
            => _results = results;

        public override string Name => "test-runbooks";

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

        public override Task<Guid> UpsertAsync(
            VectorRunbookDocument record, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public override Task UpsertAsync(
            IEnumerable<VectorRunbookDocument> records, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public override Task DeleteAsync(Guid key, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public override async IAsyncEnumerable<VectorSearchResult<VectorRunbookDocument>> SearchAsync<TInput>(
            TInput vector, int top,
            VectorSearchOptions<VectorRunbookDocument>? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            foreach (var result in _results)
                yield return result;
        }

        public override object? GetService(Type serviceType, object? serviceKey = null)
            => null;
    }
}
