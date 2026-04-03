namespace OpsCopilot.AgentRuns.Presentation.Contracts;

/// <summary>Response body for a successfully submitted feedback.</summary>
/// <param name="FeedbackId">Unique identifier of the persisted feedback record.</param>
/// <param name="RunId">The agent run this feedback relates to.</param>
/// <param name="Rating">Rating submitted by the operator (1–5).</param>
/// <param name="Comment">Optional operator comment.</param>
/// <param name="SubmittedAtUtc">UTC timestamp when feedback was recorded.</param>
/// <param name="PromotionDecision">
/// Canary-promotion gate result for the relevant prompt experiment, or
/// <c>null</c> when no experiment is active for the run's prompt key.
/// One of: <c>"Promote"</c>, <c>"Reject"</c>, <c>"NoCanary"</c>.
/// Slice 181 — §6.16 Feedback-driven canary promotion.
/// </param>
public sealed record RunFeedbackResponse(
    Guid            FeedbackId,
    Guid            RunId,
    int             Rating,
    string?         Comment,
    DateTimeOffset  SubmittedAtUtc,
    string?         PromotionDecision = null);
