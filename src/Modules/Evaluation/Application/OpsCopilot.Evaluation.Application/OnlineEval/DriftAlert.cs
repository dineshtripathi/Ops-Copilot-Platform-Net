namespace OpsCopilot.Evaluation.Application.OnlineEval;

/// <summary>
/// Signals that the rolling-window retrieval confidence average has dropped below
/// the configured baseline threshold — indicating potential drift in the retrieval
/// pipeline.
/// Slice 168 — §6.15.
/// </summary>
internal sealed record DriftAlert(
    double          CurrentAvg,
    double          BaselineThreshold,
    int             WindowSize,
    DateTimeOffset  DetectedAt);
