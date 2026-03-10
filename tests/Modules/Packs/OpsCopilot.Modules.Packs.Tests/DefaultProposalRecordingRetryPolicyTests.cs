using OpsCopilot.Packs.Infrastructure;
using Xunit;

namespace OpsCopilot.Modules.Packs.Tests;

public sealed class DefaultProposalRecordingRetryPolicyTests
{
    private readonly DefaultProposalRecordingRetryPolicy _sut = new();

    [Fact]
    public void MaxAttempts_IsThree() =>
        Assert.Equal(3, _sut.MaxAttempts);

    [Fact]
    public void ShouldRetry_Attempt1_ReturnsTrue() =>
        Assert.True(_sut.ShouldRetry(1));

    [Fact]
    public void ShouldRetry_Attempt2_ReturnsTrue() =>
        Assert.True(_sut.ShouldRetry(2));

    [Fact]
    public void ShouldRetry_Attempt3_ReturnsTrue() =>
        Assert.True(_sut.ShouldRetry(3));

    [Fact]
    public void ShouldRetry_Attempt4_ReturnsFalse() =>
        Assert.False(_sut.ShouldRetry(4));

    [Fact]
    public void GetDelay_Attempt1_ReturnsZero() =>
        Assert.Equal(TimeSpan.Zero, _sut.GetDelay(1));

    [Fact]
    public void GetDelay_Attempt2_ReturnsOneSecond() =>
        Assert.Equal(TimeSpan.FromSeconds(1), _sut.GetDelay(2));

    [Fact]
    public void GetDelay_Attempt3_ReturnsTwoSeconds() =>
        Assert.Equal(TimeSpan.FromSeconds(2), _sut.GetDelay(3));

    [Fact]
    public void GetDelay_AttemptBeyondTable_ClampsToTwoSeconds() =>
        Assert.Equal(TimeSpan.FromSeconds(2), _sut.GetDelay(99));
}
