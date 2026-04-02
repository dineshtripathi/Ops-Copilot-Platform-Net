using OpsCopilot.BuildingBlocks.Contracts.Prompting;

namespace OpsCopilot.Prompting.Application.Services;

/// <summary>
/// Public bridge adapter: exposes the internal <see cref="PromotionGateService"/>
/// through the cross-module <see cref="IFeedbackQualityGate"/> contract.
/// This class is intentionally <c>public</c> so the DI root can register it.
/// Slice 181 — §6.16 Feedback-driven canary promotion.
/// </summary>
public sealed class PromotionGateBridge : IFeedbackQualityGate
{
    private readonly PromotionGateService _gate;

    public PromotionGateBridge(PromotionGateService gate) => _gate = gate;

    /// <inheritdoc />
    public string Evaluate(string promptKey, float qualityScore)
        => _gate.Evaluate(promptKey, qualityScore).ToString();
}
