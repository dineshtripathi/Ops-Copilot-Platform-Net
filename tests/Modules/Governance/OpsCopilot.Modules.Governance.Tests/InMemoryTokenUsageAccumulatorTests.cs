using OpsCopilot.Governance.Application.Services;
using Xunit;

namespace OpsCopilot.Modules.Governance.Tests;

public sealed class InMemoryTokenUsageAccumulatorTests
{
    private readonly InMemoryTokenUsageAccumulator _sut = new();

    [Fact]
    public void AddTokens_FirstCall_StoresTokens()
    {
        _sut.AddTokens("tenant1", "session1", 100);

        Assert.Equal(100, _sut.GetTotalTokens("tenant1", "session1"));
    }

    [Fact]
    public void AddTokens_MultipleCallsSameSession_Accumulates()
    {
        _sut.AddTokens("tenant1", "session1", 100);
        _sut.AddTokens("tenant1", "session1", 250);
        _sut.AddTokens("tenant1", "session1", 50);

        Assert.Equal(400, _sut.GetTotalTokens("tenant1", "session1"));
    }

    [Fact]
    public void AddTokens_DifferentSessions_TrackedIndependently()
    {
        _sut.AddTokens("tenant1", "session-a", 200);
        _sut.AddTokens("tenant1", "session-b", 300);

        Assert.Equal(200, _sut.GetTotalTokens("tenant1", "session-a"));
        Assert.Equal(300, _sut.GetTotalTokens("tenant1", "session-b"));
    }

    [Fact]
    public void AddTokens_DifferentTenants_TrackedIndependently()
    {
        _sut.AddTokens("tenant-x", "session1", 500);
        _sut.AddTokens("tenant-y", "session1", 150);

        Assert.Equal(500, _sut.GetTotalTokens("tenant-x", "session1"));
        Assert.Equal(150, _sut.GetTotalTokens("tenant-y", "session1"));
    }

    [Fact]
    public void GetTotalTokens_NoTokensRecorded_ReturnsZero()
    {
        var total = _sut.GetTotalTokens("unknown-tenant", "unknown-session");

        Assert.Equal(0, total);
    }

    [Fact]
    public void AddTokens_ConcurrentAdds_CorrectTotal()
    {
        const int iterations = 1000;
        const int tokensPerCall = 1;

        Parallel.For(0, iterations, _ =>
        {
            _sut.AddTokens("tenant1", "concurrent-session", tokensPerCall);
        });

        Assert.Equal(iterations * tokensPerCall, _sut.GetTotalTokens("tenant1", "concurrent-session"));
    }
}
