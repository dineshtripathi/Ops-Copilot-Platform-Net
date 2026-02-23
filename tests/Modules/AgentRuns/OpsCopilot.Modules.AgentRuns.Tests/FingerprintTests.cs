using OpsCopilot.BuildingBlocks.Domain.Services;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

/// <summary>
/// Tests that <see cref="AlertFingerprintService.Compute"/> produces a
/// stable, deterministic fingerprint that satisfies the uniqueness and
/// format requirements for the Slice 1 duplicate-detection mechanism.
/// </summary>
public sealed class FingerprintTests
{
    [Fact]
    public void SameInput_ReturnsSameFingerprint()
    {
        const string json = """{"alertId":"abc-123","severity":"High"}""";

        var first  = AlertFingerprintService.Compute(json);
        var second = AlertFingerprintService.Compute(json);

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentInputs_ReturnDifferentFingerprints()
    {
        var fp1 = AlertFingerprintService.Compute("""{"alertId":"aaa"}""");
        var fp2 = AlertFingerprintService.Compute("""{"alertId":"bbb"}""");

        Assert.NotEqual(fp1, fp2);
    }

    [Fact]
    public void Output_Is64CharUppercaseHex()
    {
        var fp = AlertFingerprintService.Compute("""{"x":1}""");

        Assert.Equal(64, fp.Length);
        Assert.Matches("^[0-9A-F]{64}$", fp);
    }

    [Fact]
    public void EmptyInput_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => AlertFingerprintService.Compute(string.Empty));
    }

    [Fact]
    public void WhitespaceVariations_ProduceDifferentFingerprints()
    {
        // Two semantically identical but bytes-different JSON strings must
        // produce different fingerprints (byte-stable, not semantic-stable).
        var compact  = AlertFingerprintService.Compute("""{"k":"v"}""");
        var indented = AlertFingerprintService.Compute("{\n  \"k\": \"v\"\n}");

        Assert.NotEqual(compact, indented);
    }
}
