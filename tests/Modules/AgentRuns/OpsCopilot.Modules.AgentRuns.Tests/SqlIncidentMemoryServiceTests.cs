using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Models;
using OpsCopilot.AgentRuns.Infrastructure.Memory;
using OpsCopilot.AgentRuns.Infrastructure.Persistence;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

public sealed class SqlIncidentMemoryServiceTests : IDisposable
{
    private readonly AgentRunsDbContext          _db;
    private readonly SqlIncidentMemoryService    _sut;

    public SqlIncidentMemoryServiceTests()
    {
        var opts = new DbContextOptionsBuilder<AgentRunsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db  = new AgentRunsDbContext(opts);
        _sut = new SqlIncidentMemoryService(_db, NullLogger<SqlIncidentMemoryService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a run, sets its Status to <paramref name="status"/> via Complete(),
    /// and backdates CreatedAtUtc to <paramref name="createdAt"/> via EF shadow write.
    /// </summary>
    private AgentRun SaveRun(
        string            tenantId,
        string            fingerprint,
        AgentRunStatus    status,
        DateTimeOffset    createdAt,
        RunContext?       context = null)
    {
        var run = AgentRun.Create(tenantId, fingerprint, sessionId: null, context: context);

        if (status is AgentRunStatus.Failed or AgentRunStatus.Degraded or AgentRunStatus.Completed)
            run.Complete(status, "{}", "[]");

        _db.AgentRuns.Add(run);

        // Bypass private setter to set the backdated timestamp.
        _db.Entry(run).Property(r => r.CreatedAtUtc).CurrentValue = createdAt;

        _db.SaveChanges();
        return run;
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecallAsync_NoRuns_ReturnsEmpty()
    {
        var result = await _sut.RecallAsync("fp", "t1");

        Assert.Empty(result);
    }

    [Fact]
    public async Task RecallAsync_OnlyCompletedRuns_ReturnsEmpty()
    {
        SaveRun("t1", "fp-ok", AgentRunStatus.Completed, DateTimeOffset.UtcNow.AddDays(-1));

        var result = await _sut.RecallAsync("fp-ok", "t1");

        Assert.Empty(result);
    }

    [Fact]
    public async Task RecallAsync_FailedRunsRecentWindow_ReturnsCitations()
    {
        SaveRun("t1", "fp-crash", AgentRunStatus.Failed, DateTimeOffset.UtcNow.AddDays(-3));

        var result = await _sut.RecallAsync("any-query", "t1");

        Assert.Single(result);
        Assert.Equal("fp-crash", result[0].AlertFingerprint);
        Assert.Contains("1 failure(s)", result[0].SummarySnippet);
    }

    [Fact]
    public async Task RecallAsync_DegradedRun_ReturnsCitation()
    {
        SaveRun("t1", "fp-degraded", AgentRunStatus.Degraded, DateTimeOffset.UtcNow.AddDays(-2));

        var result = await _sut.RecallAsync("any-query", "t1");

        Assert.Single(result);
        Assert.Equal("fp-degraded", result[0].AlertFingerprint);
    }

    [Fact]
    public async Task RecallAsync_OldRuns_ExcludesBeforeCutoff()
    {
        // 15 days ago — outside the 14-day window
        SaveRun("t1", "fp-old", AgentRunStatus.Failed, DateTimeOffset.UtcNow.AddDays(-15));

        var result = await _sut.RecallAsync("any-query", "t1");

        Assert.Empty(result);
    }

    [Fact]
    public async Task RecallAsync_MultipleFailuresSameFingerprint_GroupsCount()
    {
        var fp = "fp-repeated";
        for (var i = 1; i <= 3; i++)
            SaveRun("t1", fp, AgentRunStatus.Failed, DateTimeOffset.UtcNow.AddDays(-i));

        var result = await _sut.RecallAsync("any-query", "t1");

        Assert.Single(result);
        Assert.Equal(fp, result[0].AlertFingerprint);
        Assert.Contains("3 failure(s)", result[0].SummarySnippet);
    }

    [Fact]
    public async Task RecallAsync_DifferentTenants_IsolatesCorrectly()
    {
        SaveRun("tenant-a", "fp-a", AgentRunStatus.Failed, DateTimeOffset.UtcNow.AddDays(-1));
        SaveRun("tenant-b", "fp-b", AgentRunStatus.Failed, DateTimeOffset.UtcNow.AddDays(-1));

        var resultA = await _sut.RecallAsync("q", "tenant-a");
        var resultB = await _sut.RecallAsync("q", "tenant-b");

        Assert.Single(resultA);
        Assert.Equal("fp-a", resultA[0].AlertFingerprint);

        Assert.Single(resultB);
        Assert.Equal("fp-b", resultB[0].AlertFingerprint);
    }

    [Fact]
    public async Task RecallAsync_OrdersByFrequencyDescending()
    {
        // fp-rare: 1 hit,  fp-common: 3 hits
        SaveRun("t1", "fp-rare",   AgentRunStatus.Failed, DateTimeOffset.UtcNow.AddDays(-1));
        for (var i = 1; i <= 3; i++)
            SaveRun("t1", "fp-common", AgentRunStatus.Failed, DateTimeOffset.UtcNow.AddDays(-i));

        var result = await _sut.RecallAsync("q", "t1");

        Assert.Equal(2, result.Count);
        Assert.Equal("fp-common", result[0].AlertFingerprint);
        Assert.Equal("fp-rare",   result[1].AlertFingerprint);
    }

    [Fact]
    public async Task RecallAsync_ExceptionSignal_SnippetContainsAppException()
    {
        SaveRun("t1", "fp-ex", AgentRunStatus.Failed, DateTimeOffset.UtcNow.AddDays(-1),
            new RunContext(IsExceptionSignal: true, AlertProvider: "AzureMonitor", AzureApplication: "payments-api"));

        var result = await _sut.RecallAsync("q", "t1");

        Assert.Single(result);
        Assert.Contains("App Exception",   result[0].SummarySnippet);
        Assert.Contains("[AzureMonitor]",  result[0].SummarySnippet);
        Assert.Contains("payments-api",    result[0].SummarySnippet);
    }

    [Fact]
    public async Task RecallAsync_AlertSourceType_IncludedInSnippet()
    {
        SaveRun("t1", "fp-metric", AgentRunStatus.Failed, DateTimeOffset.UtcNow.AddDays(-1),
            new RunContext(AlertSourceType: "Metric", AlertProvider: "AzureMonitor"));

        var result = await _sut.RecallAsync("q", "t1");

        Assert.Single(result);
        Assert.Contains("Metric",         result[0].SummarySnippet);
        Assert.Contains("[AzureMonitor]", result[0].SummarySnippet);
    }

    [Fact]
    public async Task RecallAsync_AzureApplication_IncludedInSnippet()
    {
        SaveRun("t1", "fp-app", AgentRunStatus.Degraded, DateTimeOffset.UtcNow.AddDays(-2),
            new RunContext(AzureApplication: "catalog-service"));

        var result = await _sut.RecallAsync("q", "t1");

        Assert.Single(result);
        Assert.Contains("catalog-service", result[0].SummarySnippet);
    }

    [Fact]
    public async Task RecallAsync_CompletedExceptionSignal_IsRecalled()
    {
        // A Completed run that carried an exception signal still represents a real
        // past incident and must appear in recall so the LLM can ground its response.
        SaveRun("t1", "fp-resolved-ex", AgentRunStatus.Completed, DateTimeOffset.UtcNow.AddDays(-1),
            new RunContext(IsExceptionSignal: true, AlertSourceType: "Log", AlertProvider: "AzureMonitor"));

        var result = await _sut.RecallAsync("q", "t1");

        Assert.Single(result);
        Assert.Equal("fp-resolved-ex", result[0].AlertFingerprint);
        Assert.Contains("App Exception", result[0].SummarySnippet);
    }

    [Fact]
    public async Task RecallAsync_AzureResourceGroup_IncludedInSnippet()
    {
        SaveRun("t1", "fp-rg", AgentRunStatus.Failed, DateTimeOffset.UtcNow.AddDays(-1),
            new RunContext(AlertSourceType: "Metric", AzureResourceGroup: "ops-prod-rg"));

        var result = await _sut.RecallAsync("q", "t1");

        Assert.Single(result);
        Assert.Contains("ops-prod-rg", result[0].SummarySnippet);
    }

    [Fact]
    public async Task RecallAsync_AzureResourceId_LeafSegmentInSnippet()
    {
        const string resourceId =
            "/subscriptions/sub-123/resourceGroups/ops-prod-rg/providers/Microsoft.Web/sites/payments-api";

        SaveRun("t1", "fp-rid", AgentRunStatus.Degraded, DateTimeOffset.UtcNow.AddDays(-1),
            new RunContext(AzureResourceId: resourceId));

        var result = await _sut.RecallAsync("q", "t1");

        Assert.Single(result);
        // Only the leaf resource name should appear, not the full subscription path.
        Assert.Contains("payments-api", result[0].SummarySnippet);
        Assert.DoesNotContain("/subscriptions/", result[0].SummarySnippet);
    }
}
