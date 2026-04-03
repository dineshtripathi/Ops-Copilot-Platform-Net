using OpsCopilot.BuildingBlocks.Contracts.Prompting;
using OpsCopilot.Prompting.Application.Models;
using OpsCopilot.Prompting.Application.Services;
using Xunit;

namespace OpsCopilot.Modules.Prompting.Tests;

/// <summary>
/// Unit tests for <see cref="PromotionGateBridge"/>.
/// Verifies that the bridge correctly delegates to the internal
/// <see cref="PromotionGateService"/> and surfaces results as
/// the string representation expected by the cross-module contract.
/// Slice 181 — §6.16 Feedback-driven canary promotion.
/// </summary>
public sealed class PromotionGateBridgeTests
{
    /// <summary>
    /// Builds a bridge wired to a real <see cref="InMemoryCanaryStore"/>
    /// (optionally pre-seeded with a canary for <paramref name="promptKey"/>).
    /// </summary>
    private static PromotionGateBridge CreateBridge(
        string? promptKey = null, string? canaryVersionId = null)
    {
        var store = new InMemoryCanaryStore();
        if (promptKey is not null)
            store.SetCanary(promptKey, new CanaryState(
                PromptKey:        promptKey,
                CandidateVersion: 2,
                CandidateContent: canaryVersionId ?? "v-canary",
                TrafficPercent:   50,
                StartedAt:        DateTimeOffset.UtcNow));

        var gate = new PromotionGateService(store);
        return new PromotionGateBridge(gate);
    }

    // ── Promote ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1.0f)]   // perfect score
    [InlineData(0.7f)]   // at threshold (inclusive)
    public void Evaluate_ScoreAtOrAboveThreshold_ReturnsPromote(float score)
    {
        var bridge = CreateBridge("key1");

        var result = bridge.Evaluate("key1", score);

        Assert.Equal("Promote", result);
    }

    // ── Reject ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0f)]    // worst possible
    [InlineData(0.69f)]   // just below threshold
    public void Evaluate_ScoreBelowThreshold_ReturnsReject(float score)
    {
        var bridge = CreateBridge("key2");

        var result = bridge.Evaluate("key2", score);

        Assert.Equal("Reject", result);
    }

    // ── NoCanary ────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_NoCanaryRegistered_ReturnsNoCanary()
    {
        // Store is empty — no canary for any key.
        var bridge = CreateBridge();   // no promptKey seeded

        var result = bridge.Evaluate("nonexistent-prompt", 0.9f);

        Assert.Equal("NoCanary", result);
    }

    // ── Contract ────────────────────────────────────────────────────────────

    [Fact]
    public void PromotionGateBridge_ImplementsIFeedbackQualityGate()
    {
        var bridge = CreateBridge();

        Assert.IsAssignableFrom<IFeedbackQualityGate>(bridge);
    }

    // ── Isolation: different keys are independent ────────────────────────────

    [Fact]
    public void Evaluate_TwoPromptKeys_AreEvaluatedIndependently()
    {
        var store = new InMemoryCanaryStore();
        store.SetCanary("with-canary", new CanaryState(
            PromptKey:        "with-canary",
            CandidateVersion: 2,
            CandidateContent: "v2",
            TrafficPercent:   50,
            StartedAt:        DateTimeOffset.UtcNow));
        // "no-canary" key is not seeded.
        var bridge = new PromotionGateBridge(new PromotionGateService(store));

        var withCanary    = bridge.Evaluate("with-canary",  0.8f);
        var withoutCanary = bridge.Evaluate("no-canary",    0.8f);

        Assert.Equal("Promote",  withCanary);
        Assert.Equal("NoCanary", withoutCanary);
    }
}
