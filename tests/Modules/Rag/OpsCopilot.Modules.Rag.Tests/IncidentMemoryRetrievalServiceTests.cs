using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.VectorData;
using Moq;
using OpsCopilot.Rag.Application.Memory;
using OpsCopilot.Rag.Domain;
using OpsCopilot.Rag.Infrastructure.Memory;
using Xunit;

namespace OpsCopilot.Modules.Rag.Tests;

public sealed class IncidentMemoryRetrievalServiceTests
{
    // ── InMemoryIncidentMemoryRetrievalService ────────────────────────────

    [Fact]
    public async Task InMemoryService_SearchAsync_ReturnsEmpty()
    {
        var sut    = new InMemoryIncidentMemoryRetrievalService();
        var query  = new IncidentMemoryQuery("cpu spike", "tenant-a");
        var result = await sut.SearchAsync(query);
        Assert.Empty(result);
    }

    // ── VectorIncidentMemoryRetrievalService ─────────────────────────────

    [Fact]
    public async Task VectorService_SearchAsync_FiltersWrongTenant()
    {
        var doc = new IncidentMemoryDocument
        {
            Id               = Guid.NewGuid(),
            TenantId         = "other-tenant",
            AlertFingerprint = "fp-001",
            RunId            = "run-001",
            SummaryText      = "CPU spiked on prod.",
            CreatedAtUtc     = DateTimeOffset.UtcNow,
        };
        var collection = new FakeCollection(
            [new VectorSearchResult<IncidentMemoryDocument>(doc, 0.95)]);

        var embedder = new Mock<IEmbeddingGenerator<string, Embedding<float>>>(MockBehavior.Strict);
        embedder
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                [new Embedding<float>(new ReadOnlyMemory<float>(new float[] { 0.1f }))]));

        var sut   = new VectorIncidentMemoryRetrievalService(
            embedder.Object, collection, NullLogger<VectorIncidentMemoryRetrievalService>.Instance);
        var query = new IncidentMemoryQuery("cpu spike", "my-tenant");

        var result = await sut.SearchAsync(query);

        Assert.Empty(result);
    }

    [Fact]
    public async Task VectorService_SearchAsync_FiltersLowScore()
    {
        var doc = new IncidentMemoryDocument
        {
            Id               = Guid.NewGuid(),
            TenantId         = "my-tenant",
            AlertFingerprint = "fp-002",
            RunId            = "run-002",
            SummaryText      = "Memory leak detected.",
            CreatedAtUtc     = DateTimeOffset.UtcNow,
        };
        var collection = new FakeCollection(
            [new VectorSearchResult<IncidentMemoryDocument>(doc, 0.5)]); // below MinScore 0.7

        var embedder = new Mock<IEmbeddingGenerator<string, Embedding<float>>>(MockBehavior.Strict);
        embedder
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                [new Embedding<float>(new ReadOnlyMemory<float>(new float[] { 0.2f }))]));

        var sut   = new VectorIncidentMemoryRetrievalService(
            embedder.Object, collection, NullLogger<VectorIncidentMemoryRetrievalService>.Instance);
        var query = new IncidentMemoryQuery("memory leak", "my-tenant");

        var result = await sut.SearchAsync(query);

        Assert.Empty(result);
    }

    [Fact]
    public async Task VectorService_SearchAsync_EmbedderThrows_ReturnsEmpty()
    {
        var collection = new FakeCollection([]);
        var embedder   = new Mock<IEmbeddingGenerator<string, Embedding<float>>>(MockBehavior.Strict);
        embedder
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("embedding service unavailable"));

        var sut   = new VectorIncidentMemoryRetrievalService(
            embedder.Object, collection, NullLogger<VectorIncidentMemoryRetrievalService>.Instance);
        var query = new IncidentMemoryQuery("disk full", "tenant-b");

        var result = await sut.SearchAsync(query);

        Assert.Empty(result);
    }

    // ── FakeCollection ────────────────────────────────────────────────────

    private sealed class FakeCollection : VectorStoreCollection<Guid, IncidentMemoryDocument>
    {
        private readonly IReadOnlyList<VectorSearchResult<IncidentMemoryDocument>> _results;

        public FakeCollection(IReadOnlyList<VectorSearchResult<IncidentMemoryDocument>> results)
            => _results = results;

        public override string Name => "test";

        public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public override Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public override Task<IncidentMemoryDocument?> GetAsync(
            Guid key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public override IAsyncEnumerable<IncidentMemoryDocument> GetAsync(
            Expression<Func<IncidentMemoryDocument, bool>> filter, int top,
            FilteredRecordRetrievalOptions<IncidentMemoryDocument>? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public override Task<Guid> UpsertAsync(
            IncidentMemoryDocument record, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public override Task UpsertAsync(
            IEnumerable<IncidentMemoryDocument> records, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public override Task DeleteAsync(Guid key, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public override async IAsyncEnumerable<VectorSearchResult<IncidentMemoryDocument>> SearchAsync<TInput>(
            TInput vector, int top,
            VectorSearchOptions<IncidentMemoryDocument>? options = null,
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
