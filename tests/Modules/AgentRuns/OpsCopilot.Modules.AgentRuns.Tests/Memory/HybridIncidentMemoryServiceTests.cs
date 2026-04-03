using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.AgentRuns.Infrastructure.Memory;
using OpsCopilot.AgentRuns.Infrastructure.Persistence;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.Rag.Application.Memory;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests.Memory;

/// <summary>
/// Unit tests for HybridIncidentMemoryService (Slice 178).
/// Verifies the SQL → RAG → KQL priority chain.
/// </summary>
public sealed class HybridIncidentMemoryServiceTests : IDisposable
{
    private readonly AgentRunsDbContext                     _db;
    private readonly SqlIncidentMemoryService               _sqlSvc;
    private readonly Mock<IIncidentMemoryRetrievalService>  _ragMock;
    private readonly RagBackedIncidentMemoryService         _ragSvc;
    private readonly Mock<IPackEvidenceExecutor>            _kqlMock;
    private readonly LiveKqlIncidentMemoryService           _kqlSvc;
    private readonly HybridIncidentMemoryService            _sut;

    public HybridIncidentMemoryServiceTests()
    {
        var opts = new DbContextOptionsBuilder<AgentRunsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db      = new AgentRunsDbContext(opts);
        _sqlSvc  = new SqlIncidentMemoryService(_db, NullLogger<SqlIncidentMemoryService>.Instance);

        _ragMock = new Mock<IIncidentMemoryRetrievalService>();
        _ragSvc  = new RagBackedIncidentMemoryService(_ragMock.Object);

        _kqlMock = new Mock<IPackEvidenceExecutor>();
        _kqlSvc  = new LiveKqlIncidentMemoryService(_kqlMock.Object, NullLogger<LiveKqlIncidentMemoryService>.Instance);

        _sut = new HybridIncidentMemoryService(
            _sqlSvc, _ragSvc, _kqlSvc, NullLogger<HybridIncidentMemoryService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task SeedCompletedRunAsync(string tenantId, string fingerprint)
    {
        var run = OpsCopilot.AgentRuns.Domain.Entities.AgentRun.Create(tenantId, fingerprint, null);
        run.Complete(OpsCopilot.AgentRuns.Domain.Enums.AgentRunStatus.Failed, "{}", "[]");
        _db.AgentRuns.Add(run);
        await _db.SaveChangesAsync();
    }

    private void SetupRagHits(params IncidentMemoryHit[] hits)
    {
        _ragMock
            .Setup(r => r.SearchAsync(It.IsAny<IncidentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hits);
    }

    private void SetupKqlItems(params (string pack, string collector, int rows)[] items)
    {
        var packItems = items
            .Select(i => new PackEvidenceItem(
                PackName:      i.pack,
                CollectorId:   i.collector,
                ConnectorName: "connector",
                QueryFile:     null,
                QueryContent:  null,
                ResultJson:    "[]",
                RowCount:      i.rows,
                ErrorMessage:  null))
            .ToArray();

        _kqlMock
            .Setup(e => e.ExecuteAsync(It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackEvidenceExecutionResult(packItems, Array.Empty<string>()));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tier 1: SQL hit short-circuits RAG and KQL
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecallAsync_SqlHasResults_ReturnsSqlAndSkipsRagAndKql()
    {
        await SeedCompletedRunAsync("tenant-A", "fp-A");

        SetupRagHits(new IncidentMemoryHit("rag-run", "tenant-A", "fp-A", "rag snippet", 0.9, DateTimeOffset.UtcNow));
        SetupKqlItems(("app-insights", "top-exceptions", 5));

        var result = await _sut.RecallAsync("fp-A", "tenant-A");

        Assert.NotEmpty(result);
        // RAG and KQL were never called because SQL returned first
        _ragMock.Verify(r => r.SearchAsync(It.IsAny<IncidentMemoryQuery>(), It.IsAny<CancellationToken>()), Times.Never);
        _kqlMock.Verify(e => e.ExecuteAsync(It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tier 2: SQL empty → RAG hit short-circuits KQL
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecallAsync_SqlEmptyRagHit_ReturnsRagAndSkipsKql()
    {
        // No DB rows: SQL returns empty
        SetupRagHits(new IncidentMemoryHit("rag-run-1", "tenant-B", "fp-B", "rag snippet", 0.85, DateTimeOffset.UtcNow));
        SetupKqlItems(("app-insights", "top-exceptions", 3));

        var result = await _sut.RecallAsync("fp-B", "tenant-B");

        Assert.Single(result);
        Assert.Equal("rag-run-1", result[0].RunId);
        _kqlMock.Verify(e => e.ExecuteAsync(It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tier 3: SQL + RAG both empty → KQL live query executes
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecallAsync_SqlAndRagEmpty_FallsBackToKql()
    {
        SetupRagHits(); // empty
        SetupKqlItems(("app-insights", "top-exceptions", 5), ("k8s-basic", "pod-status", 2));

        var result = await _sut.RecallAsync("fp-C", "tenant-C");

        Assert.Equal(2, result.Count);
        _ragMock.Verify(r => r.SearchAsync(It.IsAny<IncidentMemoryQuery>(), It.IsAny<CancellationToken>()), Times.Once);
        _kqlMock.Verify(e => e.ExecuteAsync(It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // All three tiers return empty → empty result (not an error)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecallAsync_AllTiersEmpty_ReturnsEmpty()
    {
        SetupRagHits();
        _kqlMock
            .Setup(e => e.ExecuteAsync(It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackEvidenceExecutionResult(Array.Empty<PackEvidenceItem>(), Array.Empty<string>()));

        var result = await _sut.RecallAsync("fp-D", "tenant-D");

        Assert.Empty(result);
    }
}
