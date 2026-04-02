namespace OpsCopilot.BuildingBlocks.Contracts.Prompting;

/// <summary>
/// Cross-module gate that determines whether a prompt-canary experiment
/// should be promoted, rejected, or left untouched based on a feedback
/// quality score.
/// Implemented by the Prompting module; consumed by the AgentRuns feedback
/// endpoint without requiring a direct Prompting module dependency.
/// Slice 181 — §6.16 Feedback-driven canary promotion.
/// </summary>
public interface IFeedbackQualityGate
{
    /// <summary>
    /// Evaluates whether the given <paramref name="qualityScore"/> qualifies
    /// the prompt experiment identified by <paramref name="promptKey"/> for
    /// promotion.
    /// </summary>
    /// <returns>
    /// One of: <c>"Promote"</c>, <c>"Reject"</c>, or <c>"NoCanary"</c>.
    /// </returns>
    string Evaluate(string promptKey, float qualityScore);
}
