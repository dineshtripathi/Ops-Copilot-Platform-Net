using OpsCopilot.Reporting.Domain.Models;
using OpsCopilot.Reporting.Infrastructure;
using Xunit;

namespace OpsCopilot.Modules.Reporting.Tests;

/// <summary>
/// Slice 98 — EvidenceQualityEvaluator unit tests.
/// Instantiates the internal evaluator directly via InternalsVisibleTo.
/// No I/O, no mocks, no LLM calls — purely deterministic.
/// </summary>
public sealed class EvidenceQualityEvaluatorTests
{
    private static readonly EvidenceQualityEvaluator Sut = new();

    // ─── Minimal "present" factory helpers ───────────────────────────────

    private static RunBriefing AnyBriefing() =>
        new("ok", 30.0, 0.75, 10, 2, 1, 0, 3, true, null);

    private static IncidentSynthesis AnySynthesis() =>
        new("Assessment", null, null, null, null, null);

    private static ServiceBusSignals AnySbSignals() =>
        new(0, 0, 0, []);

    private static AzureChangeSynthesis AnyAzureChange() =>
        new(0, []);

    private static ConnectivitySignals AnyConnectivity() =>
        new(0, []);

    private static AuthSignals AnyAuth() =>
        new(0, []);

    private static SimilarPriorIncident AnyIncident() =>
        new(Guid.NewGuid(), "fp", "snippet", 0.9, DateTimeOffset.UtcNow);

    private static RunRecommendation AnyRecommendation() =>
        new("KEY", "Do this");

    // ─── Strength & Completeness classification ───────────────────────────

    [Fact]
    public void Evaluate_AllEightFamiliesPresent_ReturnsStrongAndComplete()
    {
        var result = Sut.Evaluate(
            AnyBriefing(), AnySynthesis(), AnySbSignals(), AnyAzureChange(),
            AnyConnectivity(), AnyAuth(),
            [AnyIncident()], [AnyRecommendation()]);

        Assert.Equal(EvidenceStrength.Strong,       result.Strength);
        Assert.Equal(EvidenceCompleteness.Complete,  result.Completeness);
        Assert.Equal(8, result.SignalFamiliesPresent);
        Assert.Equal(8, result.SignalFamiliesTotal);
        Assert.Empty(result.MissingAreas);
    }

    [Fact]
    public void Evaluate_FiveFamiliesPresent_ReturnsStrongAndPartial()
    {
        // briefing + synthesis + sb + azureChange + connectivity = 5
        var result = Sut.Evaluate(
            AnyBriefing(), AnySynthesis(), AnySbSignals(), AnyAzureChange(),
            AnyConnectivity(), null, null, null);

        Assert.Equal(EvidenceStrength.Strong,      result.Strength);
        Assert.Equal(EvidenceCompleteness.Partial, result.Completeness);
        Assert.Equal(5, result.SignalFamiliesPresent);
    }

    [Fact]
    public void Evaluate_ThreeFamiliesPresent_ReturnsModerateAndPartial()
    {
        // briefing + synthesis + sb = 3
        var result = Sut.Evaluate(
            AnyBriefing(), AnySynthesis(), AnySbSignals(), null,
            null, null, null, null);

        Assert.Equal(EvidenceStrength.Moderate,    result.Strength);
        Assert.Equal(EvidenceCompleteness.Partial, result.Completeness);
        Assert.Equal(3, result.SignalFamiliesPresent);
    }

    [Fact]
    public void Evaluate_OneFamilyPresent_ReturnsWeakAndSparse()
    {
        // briefing only = 1
        var result = Sut.Evaluate(
            AnyBriefing(), null, null, null,
            null, null, null, null);

        Assert.Equal(EvidenceStrength.Weak,       result.Strength);
        Assert.Equal(EvidenceCompleteness.Sparse, result.Completeness);
        Assert.Equal(1, result.SignalFamiliesPresent);
    }

    [Fact]
    public void Evaluate_ZeroFamiliesPresent_ReturnsInsufficientAndSparse()
    {
        var result = Sut.Evaluate(null, null, null, null, null, null, null, null);

        Assert.Equal(EvidenceStrength.Insufficient, result.Strength);
        Assert.Equal(EvidenceCompleteness.Sparse,   result.Completeness);
        Assert.Equal(0, result.SignalFamiliesPresent);
    }

    // ─── Missing-areas ordering ───────────────────────────────────────────

    [Fact]
    public void Evaluate_AllFamiliesAbsent_MissingAreasContainsAllEightInOrder()
    {
        var result = Sut.Evaluate(null, null, null, null, null, null, null, null);

        Assert.Equal(new[]
        {
            "Triage Briefing",
            "Incident Correlation",
            "Service Bus Signals",
            "Azure Change Signals",
            "Connectivity Signals",
            "Auth Signals",
            "Similar Prior Incidents",
            "Run Recommendations"
        }, result.MissingAreas);
    }

    [Fact]
    public void Evaluate_EmptyPriorIncidentsList_CountsAsAbsent()
    {
        // IReadOnlyList<SimilarPriorIncident> with Count == 0 → not present
        var result = Sut.Evaluate(
            AnyBriefing(), null, null, null, null, null,
            (IReadOnlyList<SimilarPriorIncident>)[], null);

        Assert.Contains("Similar Prior Incidents", result.MissingAreas);
    }

    [Fact]
    public void Evaluate_EmptyRecommendationsList_CountsAsAbsent()
    {
        // IReadOnlyList<RunRecommendation> with Count == 0 → not present
        var result = Sut.Evaluate(
            AnyBriefing(), null, null, null, null, null, null,
            (IReadOnlyList<RunRecommendation>)[]);

        Assert.Contains("Run Recommendations", result.MissingAreas);
    }

    // ─── Guidance strings ─────────────────────────────────────────────────

    [Theory]
    [InlineData(EvidenceStrength.Strong,       "Evidence is comprehensive. Proceed with confidence.")]
    [InlineData(EvidenceStrength.Moderate,     "Evidence is sufficient for initial triage. Consider filling gaps before executing changes.")]
    [InlineData(EvidenceStrength.Weak,         "Evidence is limited. Gather additional signal families before acting.")]
    [InlineData(EvidenceStrength.Insufficient, "No signal families are available. Manual investigation is required.")]
    public void Evaluate_ReturnsCorrectGuidanceForStrength(EvidenceStrength expectedStrength, string expectedGuidance)
    {
        // Craft input that yields exactly the target strength
        var (briefing, synthesis, sb, azureChange, connectivity) = expectedStrength switch
        {
            EvidenceStrength.Strong       => (AnyBriefing(), AnySynthesis(), AnySbSignals(), AnyAzureChange(), AnyConnectivity()),
            EvidenceStrength.Moderate     => (AnyBriefing(), AnySynthesis(), AnySbSignals(), (AzureChangeSynthesis?)null,     (ConnectivitySignals?)null),
            EvidenceStrength.Weak         => (AnyBriefing(), (IncidentSynthesis?)null, (ServiceBusSignals?)null, null, null),
            EvidenceStrength.Insufficient => ((RunBriefing?)null, null, null, null, null),
            _                             => throw new ArgumentOutOfRangeException(nameof(expectedStrength))
        };

        var result = Sut.Evaluate(briefing, synthesis, sb, azureChange, connectivity, null, null, null);

        Assert.Equal(expectedStrength, result.Strength);
        Assert.Equal(expectedGuidance, result.Guidance);
    }

    // ─── Timestamp freshness ──────────────────────────────────────────────

    [Fact]
    public void Evaluate_EvaluatedAt_IsWithinFiveSecondsOfNow()
    {
        var before = DateTimeOffset.UtcNow;
        var result = Sut.Evaluate(null, null, null, null, null, null, null, null);
        var after  = DateTimeOffset.UtcNow;

        Assert.True(result.EvaluatedAt >= before.AddSeconds(-1),
            $"EvaluatedAt {result.EvaluatedAt} is before 'before' {before}");
        Assert.True(result.EvaluatedAt <= after.AddSeconds(1),
            $"EvaluatedAt {result.EvaluatedAt} is after 'after' {after}");
    }
}
