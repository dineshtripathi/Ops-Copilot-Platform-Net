namespace OpsCopilot.Evaluation.Application.OnlineEval;

/// <summary>
/// No-op recorder used when online evaluation is not enabled.
/// Slice 168 — §6.15.
/// </summary>
internal sealed class NullOnlineEvalRecorder : IOnlineEvalRecorder
{
    public Task RecordAsync(OnlineEvalEntry entry, CancellationToken ct = default)
        => Task.CompletedTask;
}
