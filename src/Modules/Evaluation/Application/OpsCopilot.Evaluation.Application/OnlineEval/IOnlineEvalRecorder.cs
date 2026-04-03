namespace OpsCopilot.Evaluation.Application.OnlineEval;

/// <summary>
/// Records a live-traffic evaluation entry for retrieval confidence and feedback scoring.
/// Slice 168 — §6.15.
/// </summary>
public interface IOnlineEvalRecorder
{
    Task RecordAsync(OnlineEvalEntry entry, CancellationToken ct = default);
}
