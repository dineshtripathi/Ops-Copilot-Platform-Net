namespace OpsCopilot.Evaluation.Application.OnlineEval;

/// <summary>
/// Inspects the most-recent <paramref name="windowSize"/> entries in an
/// <see cref="InMemoryOnlineEvalRecorder"/> and returns a <see cref="DriftAlert"/>
/// if the rolling average of <see cref="OnlineEvalEntry.RetrievalConfidence"/>
/// falls below <paramref name="threshold"/>.
///
/// Returns <c>null</c> when:
///   • fewer than <paramref name="windowSize"/> entries are recorded, or
///   • the average is at or above the threshold.
///
/// Deterministic and dependency-free — no LLM calls, no I/O.
/// Slice 168 — §6.15.
/// </summary>
internal sealed class RetrievalDriftMonitor
{
    private readonly InMemoryOnlineEvalRecorder _recorder;
    private readonly double _threshold;
    private readonly int    _windowSize;

    public RetrievalDriftMonitor(
        InMemoryOnlineEvalRecorder recorder,
        double threshold  = 0.70,
        int    windowSize = 20)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        if (threshold is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(threshold), "Must be in [0,1].");
        if (windowSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowSize), "Must be positive.");

        _recorder   = recorder;
        _threshold  = threshold;
        _windowSize = windowSize;
    }

    /// <summary>
    /// Evaluates the most-recent window and returns a <see cref="DriftAlert"/>
    /// if drift is detected, or <c>null</c> otherwise.
    /// </summary>
    public DriftAlert? Evaluate()
    {
        var entries = _recorder.GetAll();
        if (entries.Count < _windowSize)
            return null;

        var window = entries
            .Skip(entries.Count - _windowSize)
            .Take(_windowSize);

        double avg = window.Average(e => e.RetrievalConfidence);

        if (avg >= _threshold)
            return null;

        return new DriftAlert(
            CurrentAvg:        avg,
            BaselineThreshold: _threshold,
            WindowSize:        _windowSize,
            DetectedAt:        DateTimeOffset.UtcNow);
    }
}
