using Microsoft.EntityFrameworkCore;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Infrastructure.Persistence;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

public sealed class AgentRunSessionTokenQueryTests : IDisposable
{
    private readonly AgentRunsDbContext _db;
    private readonly AgentRunSessionTokenQuery _sut;

    public AgentRunSessionTokenQueryTests()
    {
        var opts = new DbContextOptionsBuilder<AgentRunsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db  = new AgentRunsDbContext(opts);
        _sut = new AgentRunSessionTokenQuery(_db);
    }

    public void Dispose() => _db.Dispose();

    private static AgentRun RunWithTokens(string tenantId, Guid sessionId, int tokens)
    {
        var run = AgentRun.Create(tenantId, "fp-" + Guid.NewGuid(), sessionId);
        run.SetLedgerMetadata("gpt-4o", null, tokens / 2, tokens / 2, tokens, 0.01m);
        return run;
    }

    // ── Basic cases ──────────────────────────────────────────────────────────

    [Fact]
    public void GetSessionTokenTotal_NoRuns_ReturnsZero()
    {
        var result = _sut.GetSessionTokenTotal("tenant1", Guid.NewGuid().ToString());

        Assert.Equal(0, result);
    }

    [Fact]
    public void GetSessionTokenTotal_InvalidSessionIdFormat_ReturnsZero()
    {
        // Non-GUID string — should return 0, not throw.
        var result = _sut.GetSessionTokenTotal("tenant1", "not-a-guid");

        Assert.Equal(0, result);
    }

    [Fact]
    public void GetSessionTokenTotal_EmptySessionId_ReturnsZero()
    {
        var result = _sut.GetSessionTokenTotal("tenant1", string.Empty);

        Assert.Equal(0, result);
    }

    // ── Aggregation ──────────────────────────────────────────────────────────

    [Fact]
    public void GetSessionTokenTotal_SingleRunWithTokens_ReturnsTokenCount()
    {
        var sid = Guid.NewGuid();
        _db.AgentRuns.Add(RunWithTokens("t1", sid, 400));
        _db.SaveChanges();

        var result = _sut.GetSessionTokenTotal("t1", sid.ToString());

        Assert.Equal(400, result);
    }

    [Fact]
    public void GetSessionTokenTotal_MultipleRunsSameSession_ReturnsSum()
    {
        var sid = Guid.NewGuid();
        _db.AgentRuns.AddRange(RunWithTokens("t1", sid, 300), RunWithTokens("t1", sid, 700));
        _db.SaveChanges();

        var result = _sut.GetSessionTokenTotal("t1", sid.ToString());

        Assert.Equal(1000, result);
    }

    // ── Isolation ────────────────────────────────────────────────────────────

    [Fact]
    public void GetSessionTokenTotal_RunsFromDifferentSessions_IsolatesCorrectly()
    {
        var sidA = Guid.NewGuid();
        var sidB = Guid.NewGuid();
        _db.AgentRuns.AddRange(RunWithTokens("t1", sidA, 400), RunWithTokens("t1", sidB, 999));
        _db.SaveChanges();

        Assert.Equal(400, _sut.GetSessionTokenTotal("t1", sidA.ToString()));
        Assert.Equal(999, _sut.GetSessionTokenTotal("t1", sidB.ToString()));
    }

    [Fact]
    public void GetSessionTokenTotal_RunsFromDifferentTenants_IsolatesCorrectly()
    {
        var sid = Guid.NewGuid();
        _db.AgentRuns.AddRange(RunWithTokens("tenantA", sid, 500), RunWithTokens("tenantB", sid, 800));
        _db.SaveChanges();

        Assert.Equal(500, _sut.GetSessionTokenTotal("tenantA", sid.ToString()));
        Assert.Equal(800, _sut.GetSessionTokenTotal("tenantB", sid.ToString()));
    }

    // ── Null handling ─────────────────────────────────────────────────────────

    [Fact]
    public void GetSessionTokenTotal_RunsWithNullTotalTokens_ExcludesNulls()
    {
        var sid = Guid.NewGuid();
        var runWithTokens    = RunWithTokens("t1", sid, 600);
        var runWithoutTokens = AgentRun.Create("t1", "fp-null", sid); // No SetLedgerMetadata → TotalTokens is null
        _db.AgentRuns.AddRange(runWithTokens, runWithoutTokens);
        _db.SaveChanges();

        Assert.Equal(600, _sut.GetSessionTokenTotal("t1", sid.ToString()));
    }
}
