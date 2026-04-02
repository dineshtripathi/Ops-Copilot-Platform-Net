using OpsCopilot.Evaluation.Application.OnlineEval;
using Xunit;

namespace OpsCopilot.Modules.Evaluation.Tests;

/// <summary>
/// Slice 168 — §6.15 Online Eval + Retrieval Drift Monitoring.
/// Covers InMemoryOnlineEvalRecorder, NullOnlineEvalRecorder, and RetrievalDriftMonitor.
/// </summary>
public sealed class OnlineEvalTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static OnlineEvalEntry MakeEntry(double confidence, float? feedback = null)
        => new(
            RunId:               Guid.NewGuid(),
            RetrievalConfidence: confidence,
            FeedbackScore:       feedback,
            ModelVersion:        "gpt-4o",
            PromptVersionId:     "v1",
            RecordedAt:          DateTimeOffset.UtcNow);

    // ── InMemoryOnlineEvalRecorder ────────────────────────────────────────────

    [Fact]
    public async Task Recorder_StoresEntry()
    {
        var recorder = new InMemoryOnlineEvalRecorder();
        var entry    = MakeEntry(0.85, feedback: 1.0f);

        await recorder.RecordAsync(entry);

        var all = recorder.GetAll();
        Assert.Single(all);
        Assert.Equal(entry.RunId,               all[0].RunId);
        Assert.Equal(0.85,                      all[0].RetrievalConfidence);
        Assert.Equal(1.0f,                      all[0].FeedbackScore);
    }

    [Fact]
    public async Task Recorder_CapacityEnforced_DropsOldestEntries()
    {
        const int capacity = 3;
        var recorder = new InMemoryOnlineEvalRecorder(capacity);

        var entries = Enumerable.Range(1, 5)
            .Select(i => MakeEntry(i * 0.1))
            .ToList();

        foreach (var e in entries)
            await recorder.RecordAsync(e);

        var all = recorder.GetAll();
        Assert.Equal(capacity, all.Count);

        // Oldest 2 dropped; last 3 retained
        var retained = entries.Skip(2).ToList();
        for (int i = 0; i < capacity; i++)
            Assert.Equal(retained[i].RunId, all[i].RunId);
    }

    // ── NullOnlineEvalRecorder ────────────────────────────────────────────────

    [Fact]
    public async Task NullRecorder_DoesNotThrow()
    {
        var recorder = new NullOnlineEvalRecorder();
        // Must complete without any exception
        await recorder.RecordAsync(MakeEntry(0.9));
    }

    // ── RetrievalDriftMonitor ─────────────────────────────────────────────────

    [Fact]
    public async Task DriftMonitor_ReturnsDriftAlert_WhenAverageBelowThreshold()
    {
        const int    window    = 5;
        const double threshold = 0.70;

        var recorder = new InMemoryOnlineEvalRecorder();
        var monitor  = new RetrievalDriftMonitor(recorder, threshold, window);

        // All confidences well below 0.70
        foreach (var conf in new[] { 0.40, 0.45, 0.50, 0.42, 0.48 })
            await recorder.RecordAsync(MakeEntry(conf));

        var alert = monitor.Evaluate();

        Assert.NotNull(alert);
        Assert.True(alert.CurrentAvg < threshold);
        Assert.Equal(threshold,  alert.BaselineThreshold);
        Assert.Equal(window,     alert.WindowSize);
    }

    [Fact]
    public async Task DriftMonitor_ReturnsNull_WhenAverageAboveThreshold()
    {
        const int    window    = 5;
        const double threshold = 0.70;

        var recorder = new InMemoryOnlineEvalRecorder();
        var monitor  = new RetrievalDriftMonitor(recorder, threshold, window);

        // All confidences comfortably above threshold
        foreach (var conf in new[] { 0.80, 0.85, 0.90, 0.82, 0.88 })
            await recorder.RecordAsync(MakeEntry(conf));

        var alert = monitor.Evaluate();

        Assert.Null(alert);
    }
}
