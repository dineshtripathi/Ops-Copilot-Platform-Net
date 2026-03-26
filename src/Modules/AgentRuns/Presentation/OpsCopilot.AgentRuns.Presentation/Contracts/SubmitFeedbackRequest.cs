namespace OpsCopilot.AgentRuns.Presentation.Contracts;

/// <summary>Request body for POST /agent/runs/{runId}/feedback.</summary>
/// <param name="Rating">Operator rating 1 (poor) – 5 (excellent).</param>
/// <param name="Comment">Optional free-text comment; max 2 000 characters.</param>
public sealed record SubmitFeedbackRequest(int Rating, string? Comment);
