using OpsCopilot.Prompting.Application.Abstractions;
using OpsCopilot.Prompting.Application.Models;

namespace OpsCopilot.Prompting.Application.Services;

/// <summary>
/// Evaluates whether a canary prompt version meets the quality threshold
/// for promotion to the active slot or should be rejected.
/// </summary>
public sealed class PromotionGateService
{
    private readonly ICanaryStore _store;

    public const float DefaultThreshold = 0.7f;

    public PromotionGateService(ICanaryStore store) => _store = store;

    /// <summary>
    /// Returns <see cref="PromotionResult.Promote"/> when the quality score meets
    /// or exceeds the threshold, <see cref="PromotionResult.Reject"/> when it does not,
    /// or <see cref="PromotionResult.NoCanary"/> when no experiment is active.
    /// </summary>
    public PromotionResult Evaluate(string promptKey, float qualityScore, float threshold = DefaultThreshold)
    {
        var canary = _store.GetCanary(promptKey);
        if (canary is null)
            return PromotionResult.NoCanary;

        return qualityScore >= threshold
            ? PromotionResult.Promote
            : PromotionResult.Reject;
    }
}
