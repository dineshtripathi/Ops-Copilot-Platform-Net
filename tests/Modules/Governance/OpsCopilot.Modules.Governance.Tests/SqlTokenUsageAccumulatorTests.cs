using Moq;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.Governance.Application.Services;
using Xunit;

namespace OpsCopilot.Modules.Governance.Tests;

public sealed class SqlTokenUsageAccumulatorTests
{
    private readonly Mock<ISessionTokenQuery> _query = new();
    private readonly SqlTokenUsageAccumulator _sut;

    public SqlTokenUsageAccumulatorTests()
        => _sut = new SqlTokenUsageAccumulator(_query.Object);

    [Fact]
    public void AddTokens_IsNoOp_DoesNotCallQuery()
    {
        _sut.AddTokens("tenant1", "00000000-0000-0000-0000-000000000001", 500);

        _query.VerifyNoOtherCalls();
    }

    [Fact]
    public void GetTotalTokens_DelegatesToSessionTokenQuery()
    {
        _query.Setup(q => q.GetSessionTokenTotal("tenant1", "session-id")).Returns(1200);

        var result = _sut.GetTotalTokens("tenant1", "session-id");

        Assert.Equal(1200, result);
    }

    [Fact]
    public void GetTotalTokens_WhenQueryReturnsZero_ReturnsZero()
    {
        _query.Setup(q => q.GetSessionTokenTotal(It.IsAny<string>(), It.IsAny<string>())).Returns(0);

        var result = _sut.GetTotalTokens("tenant1", "any-session");

        Assert.Equal(0, result);
    }

    [Fact]
    public void AddTokens_ThenGetTotalTokens_ReturnsQueryResult_NotAccumulated()
    {
        // Verifies AddTokens truly is a no-op and doesn't internally accumulate.
        _query.Setup(q => q.GetSessionTokenTotal("t", "s")).Returns(100);

        _sut.AddTokens("t", "s", 9999); // must be ignored
        var result = _sut.GetTotalTokens("t", "s");

        Assert.Equal(100, result); // 100 from SQL, NOT 100 + 9999
    }

    [Fact]
    public void GetTotalTokens_MultipleCallsSameInput_EachDelegatesToQuery()
    {
        _query.Setup(q => q.GetSessionTokenTotal("t", "s")).Returns(50);

        _ = _sut.GetTotalTokens("t", "s");
        _ = _sut.GetTotalTokens("t", "s");

        _query.Verify(q => q.GetSessionTokenTotal("t", "s"), Times.Exactly(2));
    }
}
