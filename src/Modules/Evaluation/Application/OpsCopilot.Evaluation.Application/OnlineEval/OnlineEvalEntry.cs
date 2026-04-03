namespace OpsCopilot.Evaluation.Application.OnlineEval;

/// <summary>
/// A single recorded observation from an online (live-traffic) evaluation.
/// Captures retrieval confidence and optional user feedback for a triage run.
/// Slice 168 — §6.15 Online Eval + Drift Monitoring.
/// </summary>
public sealed record OnlineEvalEntry(
    Guid            RunId,
    double          RetrievalConfidence,
    float?          FeedbackScore,
    string          ModelVersion,
    string          PromptVersionId,
    DateTimeOffset  RecordedAt);
