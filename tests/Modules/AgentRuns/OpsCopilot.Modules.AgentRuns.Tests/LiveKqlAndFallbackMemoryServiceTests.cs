using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.AgentRuns.Infrastructure.Memory;
using OpsCopilot.AgentRuns.Infrastructure.Persistence;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// LiveKqlIncidentMemoryService
// ─────────────────────────────────────────────────────────────────────────────

public sealed class LiveKqlIncidentMemoryServiceTests
{
    private static PackEvidenceItem MakeItem(string pack, string collector, int rowCount, string? error = null)
        => new PackEvidenceItem(
            PackName:       pack,
            CollectorId:    collector,
            ConnectorName:  "test-connector",
            QueryFile:      null,
            QueryContent:   null,
            ResultJson:     rowCount > 0 ? "[]" : null,
            RowCount:       rowCount,
            ErrorMessage:   error);

    private static PackEvidenceExecutionResult MakeResult(params PackEvidenceItem[] items)
        => new PackEvidenceExecutionResult(items, Array.Empty<string>());

    [Fact]
    public async Task RecallAsync_WithPositiveRowCountItems_ReturnsCitationsForEach()
    {
        var mock = new Mock<IPackEvidenceExecutor>();
        mock.Setup(e => e.ExecuteAsync(It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult(
                MakeItem("app-insights", "top-exceptions",         5),
                MakeItem("azure-vm",     "cpu-usage",              3),
                MakeItem("k8s-basic",    "pod-status",             2)));

        var sut = new LiveKqlIncidentMemoryService(mock.Object, NullLogger<LiveKqlIncidentMemoryService>.Instance);
        var result = await sut.RecallAsync("fp-1", "tenant-1");

        Assert.Equal(3, result.Count);
        Assert.All(result, c => Assert.False(string.IsNullOrEmpty(c.RunId)));
        Assert.All(result, c => Assert.False(string.IsNullOrEmpty(c.AlertFingerprint)));
        Assert.All(result, c => Assert.Contains("result(s)", c.SummarySnippet));
        Assert.All(result, c => Assert.Equal(0.0, c.Score));
    }

    [Fact]
    public async Task RecallAsync_ItemWithZeroRowCount_Excluded()
    {
        var mock = new Mock<IPackEvidenceExecutor>();
        mock.Setup(e => e.ExecuteAsync(It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult(
                MakeItem("app-insights", "top-exceptions", 0),
                MakeItem("azure-vm",     "cpu-usage",      4)));

        var sut    = new LiveKqlIncidentMemoryService(mock.Object, NullLogger<LiveKqlIncidentMemoryService>.Instance);
        var result = await sut.RecallAsync("fp-1", "tenant-1");

        Assert.Single(result);
        Assert.Contains("cpu-usage", result[0].RunId);
    }

    [Fact]
    public async Task RecallAsync_ItemWithErrorMessage_Excluded()
    {
        var mock = new Mock<IPackEvidenceExecutor>();
        mock.Setup(e => e.ExecuteAsync(It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult(
                MakeItem("app-insights", "top-exceptions",  5, error: "workspace not configured"),
                MakeItem("k8s-basic",    "pod-status",      2)));

        var sut    = new LiveKqlIncidentMemoryService(mock.Object, NullLogger<LiveKqlIncidentMemoryService>.Instance);
        var result = await sut.RecallAsync("fp-1", "tenant-1");

        Assert.Single(result);
        Assert.Contains("pod-status", result[0].RunId);
    }

    [Fact]
    public async Task RecallAsync_AllItemsFiltered_ReturnsEmpty()
    {
        var mock = new Mock<IPackEvidenceExecutor>();
        mock.Setup(e => e.ExecuteAsync(It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult(
                MakeItem("app-insights", "top-exceptions", 0),
                MakeItem("azure-vm",     "cpu-usage",      0)));

        var sut    = new LiveKqlIncidentMemoryService(mock.Object, NullLogger<LiveKqlIncidentMemoryService>.Instance);
        var result = await sut.RecallAsync("fp-1", "tenant-1");

        Assert.Empty(result);
    }

    [Fact]
    public async Task RecallAsync_ExecutorThrows_ReturnsEmpty()
    {
        var mock = new Mock<IPackEvidenceExecutor>();
        mock.Setup(e => e.ExecuteAsync(It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("connection failed"));

        var sut    = new LiveKqlIncidentMemoryService(mock.Object, NullLogger<LiveKqlIncidentMemoryService>.Instance);
        var result = await sut.RecallAsync("fp-1", "tenant-1");

        Assert.Empty(result);
    }

    [Fact]
    public async Task RecallAsync_CitationRunId_ContainsPackAndCollectorInPath()
    {
        var mock = new Mock<IPackEvidenceExecutor>();
        mock.Setup(e => e.ExecuteAsync(It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult(MakeItem("app-insights", "top-exceptions", 7)));

        var sut    = new LiveKqlIncidentMemoryService(mock.Object, NullLogger<LiveKqlIncidentMemoryService>.Instance);
        var result = await sut.RecallAsync("fp-1", "tenant-1");

        Assert.Single(result);
        Assert.Equal("kql/app-insights/top-exceptions", result[0].RunId);
        Assert.Equal("app-insights.top-exceptions",     result[0].AlertFingerprint);
    }

    [Fact]
    public async Task RecallAsync_EmptyPackResult_ReturnsEmpty()
    {
        var mock = new Mock<IPackEvidenceExecutor>();
        mock.Setup(e => e.ExecuteAsync(It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackEvidenceExecutionResult(
                Array.Empty<PackEvidenceItem>(),
                Array.Empty<string>()));

        var sut    = new LiveKqlIncidentMemoryService(mock.Object, NullLogger<LiveKqlIncidentMemoryService>.Instance);
        var result = await sut.RecallAsync("fp-1", "tenant-1");

        Assert.Empty(result);
    }

    [Fact]
    public async Task RecallAsync_PassesTenantIdToExecutor()
    {
        PackEvidenceExecutionRequest? captured = null;
        var mock = new Mock<IPackEvidenceExecutor>();
        mock.Setup(e => e.ExecuteAsync(It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PackEvidenceExecutionRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new PackEvidenceExecutionResult(Array.Empty<PackEvidenceItem>(), Array.Empty<string>()));

        var sut = new LiveKqlIncidentMemoryService(mock.Object, NullLogger<LiveKqlIncidentMemoryService>.Instance);
        await sut.RecallAsync("fp-1", "my-tenant");

        Assert.NotNull(captured);
        Assert.Equal("my-tenant", captured!.TenantId);
        Assert.Equal("B",         captured.DeploymentMode);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// FallbackIncidentMemoryService
// ─────────────────────────────────────────────────────────────────────────────

public sealed class FallbackIncidentMemoryServiceTests : IDisposable
{
    private readonly AgentRunsDbContext           _db;
    private readonly SqlIncidentMemoryService     _sqlSvc;
    private readonly Mock<IPackEvidenceExecutor>  _executorMock;
    private readonly LiveKqlIncidentMemoryService _kqlSvc;
    private readonly FallbackIncidentMemoryService _sut;

    public FallbackIncidentMemoryServiceTests()
    {
        var opts = new DbContextOptionsBuilder<AgentRunsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db           = new AgentRunsDbContext(opts);
        _sqlSvc       = new SqlIncidentMemoryService(_db, NullLogger<SqlIncidentMemoryService>.Instance);
        _executorMock = new Mock<IPackEvidenceExecutor>();
        _kqlSvc       = new LiveKqlIncidentMemoryService(_executorMock.Object, NullLogger<LiveKqlIncidentMemoryService>.Instance);
        _sut          = new FallbackIncidentMemoryService(_sqlSvc, _kqlSvc, NullLogger<FallbackIncidentMemoryService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private void SetupKqlItems(params (string pack, string collector, int rows)[] items)
    {
        var packItems = items.Select(i => new PackEvidenceItem(
            PackName:      i.pack,
            CollectorId:   i.collector,
            ConnectorName: "connector",
            QueryFile:     null,
            QueryContent:  null,
            ResultJson:    "[]",
            RowCount:      i.rows,
            ErrorMessage:  null)).ToArray();

        _executorMock
            .Setup(e => e.ExecuteAsync(It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackEvidenceExecutionResult(packItems, Array.Empty<string>()));
    }

    [Fact]
    public async Task RecallAsync_SqlHasResults_ReturnsSqlAndDoesNotCallKql()
    {
        // Arrange: a failed run in DB for this tenant
        var run = OpsCopilot.AgentRuns.Domain.Entities.AgentRun.Create("tenant-A", "fp-A", null);
        run.Complete(OpsCopilot.AgentRuns.Domain.Enums.AgentRunStatus.Failed, "{}", "[]");
        _db.AgentRuns.Add(run);
        await _db.SaveChangesAsync();

        SetupKqlItems(("app-insights", "top-exceptions", 5));

        var result = await _sut.RecallAsync("fp-A", "tenant-A");

        // SQL path returned, executor never called
        Assert.NotEmpty(result);
        _executorMock.Verify(e => e.ExecuteAsync(It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecallAsync_SqlEmpty_CallsKqlAndReturnsCitations()
    {
        // Arrange: empty DB
        SetupKqlItems(("azure-vm", "cpu-usage", 3), ("k8s-basic", "pod-status", 2));

        var result = await _sut.RecallAsync("fp-B", "tenant-B");

        Assert.Equal(2, result.Count);
        _executorMock.Verify(e => e.ExecuteAsync(It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecallAsync_BothEmpty_ReturnsEmpty()
    {
        // Arrange: empty DB + KQL returns no rows
        SetupKqlItems(("app-insights", "top-exceptions", 0));

        var result = await _sut.RecallAsync("fp-C", "tenant-C");

        Assert.Empty(result);
    }

    [Fact]
    public async Task RecallAsync_SqlEmpty_KqlThrows_ReturnsEmpty()
    {
        // Arrange: empty DB + executor throws
        _executorMock
            .Setup(e => e.ExecuteAsync(It.IsAny<PackEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("timeout"));

        var result = await _sut.RecallAsync("fp-D", "tenant-D");

        Assert.Empty(result);
    }
}
