using OpsCopilot.Reporting.Domain.Models;
using OpsCopilot.Reporting.Infrastructure;
using Xunit;

namespace OpsCopilot.Modules.Reporting.Tests;

/// <summary>
/// Slice 99 — DecisionPackBuilder unit tests.
/// Instantiates the internal builder directly via InternalsVisibleTo.
/// No I/O, no mocks, no LLM calls — purely deterministic.
/// </summary>
public sealed class DecisionPackBuilderTests
{
    private static readonly DecisionPackBuilder Sut = new();

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

    private static ProposedNextAction AnyProposedAction() =>
        new("Do this", "Because X", "runbook");

    private static EvidenceQualityAssessment AnyQuality() =>
        new(EvidenceStrength.Moderate, EvidenceCompleteness.Partial, 4, 8, [], "Partial evidence.", DateTimeOffset.UtcNow);

    // ─── Null-briefing guard ──────────────────────────────────────────────

    [Fact]
    public void Build_NullBriefing_ReturnsNull()
    {
        var result = Sut.Build(
            null, AnySynthesis(), AnySbSignals(), AnyAzureChange(),
            AnyConnectivity(), AnyAuth(),
            [AnyIncident()], [AnyProposedAction()], AnyQuality());

        Assert.Null(result);
    }

    // ─── Happy path ───────────────────────────────────────────────────────

    [Fact]
    public void Build_AllInputsPresent_ReturnsPack()
    {
        var result = Sut.Build(
            AnyBriefing(), AnySynthesis(), AnySbSignals(), AnyAzureChange(),
            AnyConnectivity(), AnyAuth(),
            [AnyIncident()], [AnyProposedAction()], AnyQuality());

        Assert.NotNull(result);
    }

    // ─── Assessment from synthesis ────────────────────────────────────────

    [Fact]
    public void Build_NullSynthesis_PackHasNullAssessment()
    {
        var result = Sut.Build(
            AnyBriefing(), null, AnySbSignals(), AnyAzureChange(),
            AnyConnectivity(), AnyAuth(),
            null, null, null);

        Assert.NotNull(result);
        Assert.Null(result.IncidentAssessment);
    }

    // ─── Recommended actions ──────────────────────────────────────────────

    [Fact]
    public void Build_NoProposedActions_PackHasEmptyRecommendedActions()
    {
        var result = Sut.Build(
            AnyBriefing(), AnySynthesis(), null, null,
            null, null,
            null, null, null);

        Assert.NotNull(result);
        Assert.Empty(result.RecommendedActions);
    }

    [Fact]
    public void Build_WithProposedActions_PackContainsAllProposalStrings()
    {
        var a1 = new ProposedNextAction("Action one",   "Because A", "runbook");
        var a2 = new ProposedNextAction("Action two",   "Because B", "kql");

        var result = Sut.Build(
            AnyBriefing(), AnySynthesis(), null, null,
            null, null,
            null, [a1, a2], null);

        Assert.NotNull(result);
        Assert.Equal(2, result.RecommendedActions.Count);
        Assert.Contains("Action one", result.RecommendedActions);
        Assert.Contains("Action two", result.RecommendedActions);
    }

    // ─── Evidence strength from quality ──────────────────────────────────

    [Fact]
    public void Build_NullQuality_PackUsesInsufficientStrength()
    {
        var result = Sut.Build(
            AnyBriefing(), null, null, null,
            null, null,
            null, null, null);

        Assert.NotNull(result);
        Assert.Equal(EvidenceStrength.Insufficient, result.EvidenceStrength);
    }

    [Fact]
    public void Build_WithQuality_PackUsesQualityStrengthAndGuidance()
    {
        var quality = new EvidenceQualityAssessment(
            EvidenceStrength.Strong, EvidenceCompleteness.Complete,
            8, 8, [], "Good evidence.", DateTimeOffset.UtcNow);

        var result = Sut.Build(
            AnyBriefing(), AnySynthesis(), AnySbSignals(), AnyAzureChange(),
            AnyConnectivity(), AnyAuth(),
            [AnyIncident()], [AnyProposedAction()], quality);

        Assert.NotNull(result);
        Assert.Equal(EvidenceStrength.Strong, result.EvidenceStrength);
        Assert.Equal("Good evidence.", result.EvidenceGuidance);
    }

    // ─── Key findings ─────────────────────────────────────────────────────

    [Fact]
    public void Build_WithFailureSignal_PackKeyFindingsContainsSignal()
    {
        var briefing = new RunBriefing("critical", 10.0, 1.0, 0, 0, 0, 0, 0, false, "Disk full");

        var result = Sut.Build(
            briefing, null, null, null,
            null, null,
            null, null, null);

        Assert.NotNull(result);
        Assert.Contains("Disk full", result.KeyFindings);
    }

    [Fact]
    public void Build_WithKqlRows_PackKeyFindingsContainsKqlEntry()
    {
        var briefing = new RunBriefing("warning", null, 1.0, 42, 0, 0, 0, 0, false, null);

        var result = Sut.Build(
            briefing, null, null, null,
            null, null,
            null, null, null);

        Assert.NotNull(result);
        Assert.Contains(result.KeyFindings, f => f.Contains("42") && f.Contains("KQL"));
    }

    [Fact]
    public void Build_WithPriorIncidents_PackKeyFindingsContainsIncidentCount()
    {
        var result = Sut.Build(
            AnyBriefing(), null, null, null,
            null, null,
            [AnyIncident(), AnyIncident()], null, null);

        Assert.NotNull(result);
        Assert.Contains(result.KeyFindings, f => f.Contains("2") && f.Contains("prior incident"));
    }

    // ─── Generated-at freshness ───────────────────────────────────────────

    [Fact]
    public void Build_GeneratedAtIsRecent()
    {
        var before = DateTimeOffset.UtcNow;

        var result = Sut.Build(
            AnyBriefing(), AnySynthesis(), null, null,
            null, null,
            null, null, null);

        var after = DateTimeOffset.UtcNow;

        Assert.NotNull(result);
        Assert.True(result.GeneratedAt >= before && result.GeneratedAt <= after);
    }

    // ─── Pack version ─────────────────────────────────────────────────────

    [Fact]
    public void Build_PackVersionIsDefault_OnePointZero()
    {
        var result = Sut.Build(
            AnyBriefing(), null, null, null,
            null, null,
            null, null, null);

        Assert.NotNull(result);
        Assert.Equal("1.0", result.PackVersion);
    }
}
